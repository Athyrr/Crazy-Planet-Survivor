using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// Rebuilds the flow field at a configurable interval.
/// The field is a 2D grid projected onto the planet-surface tangent plane at the player's position.
/// Each cell stores a direction vector pointing toward the lowest-cost path to the player.
///
/// Pipeline per rebuild:
///   1. BuildCostFieldJob   (parallel) — mark cells blocked by obstacles.
///   2. IntegrateFieldJob   (single)   — BFS from goal cell, fills integration costs.
///   3. BuildDirectionFieldJob (parallel) — derive direction from cheapest neighbor per cell.
///   4. CopyToCellBufferJob (single)   — write direction + cost to the DynamicBuffer.
/// </summary>
[UpdateInGroup(typeof(CustomUpdateGroup))]
[BurstCompile]
public partial struct FlowFieldSystem : ISystem
{
    // Persistent arrays reused across frames to avoid per-rebuild allocation pressure
    private NativeArray<byte> _costField;
    private NativeArray<ushort> _integrationField;
    private NativeArray<float3> _directionField;

    private EntityQuery _obstacleQuery;
    private ComponentLookup<LocalTransform> _transformLookup;
    private BufferLookup<FlowFieldCell> _cellBufferLookup;

    private bool _isInitialized;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowFieldData>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();

        var builder = new EntityQueryBuilder(Allocator.Temp);
        builder.WithAll<Obstacle, LocalTransform>();
        _obstacleQuery = state.GetEntityQuery(builder);
        builder.Dispose();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        _cellBufferLookup = state.GetBufferLookup<FlowFieldCell>(isReadOnly: false);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_isInitialized)
        {
            _costField.Dispose();
            _integrationField.Dispose();
            _directionField.Dispose();
            _isInitialized = false;
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();
        ref var flowFieldData = ref SystemAPI.GetComponentRW<FlowFieldData>(flowFieldEntity).ValueRW;

        int totalCells = flowFieldData.GridWidth * flowFieldData.GridHeight;

        // --- Initialize persistent arrays on first run ---
        if (!_isInitialized)
        {
            _costField = new NativeArray<byte>(totalCells, Allocator.Persistent);
            _integrationField = new NativeArray<ushort>(totalCells, Allocator.Persistent);
            _directionField = new NativeArray<float3>(totalCells, Allocator.Persistent);
            _isInitialized = true;
        }

        // --- Timer check ---
        flowFieldData.TimeSinceLastRebuild += SystemAPI.Time.DeltaTime;
        if (flowFieldData.TimeSinceLastRebuild < flowFieldData.RebuildInterval)
            return;

        flowFieldData.TimeSinceLastRebuild = 0f;

        // --- Gather inputs ---
        _transformLookup.Update(ref state);
        _cellBufferLookup.Update(ref state);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        float3 playerPos = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
        var planetData = SystemAPI.GetSingleton<PlanetData>();
        float3 planetCenter = planetData.Center;

        int gridWidth = flowFieldData.GridWidth;
        int gridHeight = flowFieldData.GridHeight;
        float cellSize = flowFieldData.CellSize;

        // --- Compute grid orientation (tangent plane at player position) ---
        float3 gridNormal = math.normalize(playerPos - planetCenter);
        float3 gridRight = math.normalize(math.cross(gridNormal, new float3(0f, 1f, 0f)));
        if (math.lengthsq(gridRight) < 0.001f)
            gridRight = math.normalize(math.cross(gridNormal, new float3(1f, 0f, 0f)));
        float3 gridForward = math.normalize(math.cross(gridRight, gridNormal));

        // Store grid metadata in the singleton
        flowFieldData.Origin = playerPos;
        flowFieldData.GridRight = gridRight;
        flowFieldData.GridForward = gridForward;
        flowFieldData.GridNormal = gridNormal;

        // --- Gather obstacle data on main thread (counts are typically low) ---
        int obstacleCount = _obstacleQuery.CalculateEntityCount();
        var obstaclePositions = new NativeArray<float3>(obstacleCount, Allocator.TempJob);
        var obstacleRadii = new NativeArray<float>(obstacleCount, Allocator.TempJob);

        var obstacleEntities = _obstacleQuery.ToEntityArray(Allocator.Temp);
        var obstacleComponents = _obstacleQuery.ToComponentDataArray<Obstacle>(Allocator.Temp);
        for (int i = 0; i < obstacleCount; i++)
        {
            obstaclePositions[i] = _transformLookup[obstacleEntities[i]].Position;
            obstacleRadii[i] = obstacleComponents[i].AvoidanceRadius;
        }

        obstacleEntities.Dispose();
        obstacleComponents.Dispose();

        // Pre-size the cell buffer before scheduling jobs so AsNativeArray is stable
        var cellBuffer = SystemAPI.GetBuffer<FlowFieldCell>(flowFieldEntity);
        cellBuffer.ResizeUninitialized(totalCells);

        // --- Phase 1: Build cost field (parallel) ---
        var buildCostJob = new BuildCostFieldJob
        {
            GridWidth = gridWidth,
            GridHeight = gridHeight,
            CellSize = cellSize,
            Origin = playerPos,
            GridRight = gridRight,
            GridForward = gridForward,
            ObstaclePositions = obstaclePositions,
            ObstacleRadii = obstacleRadii,
            CostField = _costField
        };
        state.Dependency = buildCostJob.Schedule(totalCells, 64, state.Dependency);

        // --- Phase 2: BFS integration (sequential) ---
        int goalIndex = (gridHeight / 2) * gridWidth + (gridWidth / 2);

        var integrateJob = new IntegrateFieldJob
        {
            GridWidth = gridWidth,
            GridHeight = gridHeight,
            CostField = _costField,
            IntegrationField = _integrationField,
            GoalIndex = goalIndex
        };
        state.Dependency = integrateJob.Schedule(state.Dependency);

        // --- Phase 3: Build direction field (parallel) ---
        var buildDirJob = new BuildDirectionFieldJob
        {
            GridWidth = gridWidth,
            GridHeight = gridHeight,
            GridRight = gridRight,
            GridForward = gridForward,
            IntegrationField = _integrationField,
            DirectionField = _directionField
        };
        state.Dependency = buildDirJob.Schedule(totalCells, 64, state.Dependency);

        // --- Phase 4: Copy to DynamicBuffer (sequential) ---
        var copyJob = new CopyToCellBufferJob
        {
            DirectionField = _directionField,
            CostField = _costField,
            CellBufferLookup = _cellBufferLookup,
            FlowFieldEntity = flowFieldEntity
        };
        state.Dependency = copyJob.Schedule(state.Dependency);

        // Dispose temp arrays after jobs are done
        state.Dependency = obstaclePositions.Dispose(state.Dependency);
        state.Dependency = obstacleRadii.Dispose(state.Dependency);

        flowFieldData.IsReady = true;
    }

    // --------------------------------------------------------------------------------------------
    // JOBS
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Marks each grid cell as passable (cost=1) or blocked (cost=255) based on obstacle proximity.
    /// </summary>
    [BurstCompile]
    private struct BuildCostFieldJob : IJobParallelFor
    {
        public int GridWidth;
        public int GridHeight;
        public float CellSize;
        public float3 Origin;
        public float3 GridRight;
        public float3 GridForward;

        [ReadOnly] public NativeArray<float3> ObstaclePositions;
        [ReadOnly] public NativeArray<float> ObstacleRadii;

        [NativeDisableParallelForRestriction] public NativeArray<byte> CostField;

        public void Execute(int index)
        {
            int cx = index % GridWidth;
            int cy = index / GridWidth;
            float cellLocalX = (cx - GridWidth / 2) * CellSize;
            float cellLocalZ = (cy - GridHeight / 2) * CellSize;

            byte cost = 1;
            float halfCell = CellSize * 0.5f;

            for (int i = 0; i < ObstaclePositions.Length; i++)
            {
                // Project obstacle onto tangent plane (ignore normal component)
                // to get a true 2D distance regardless of planet curvature.
                float3 delta = ObstaclePositions[i] - Origin;
                float obsLocalX = math.dot(delta, GridRight);
                float obsLocalZ = math.dot(delta, GridForward);
                float dx = cellLocalX - obsLocalX;
                float dz = cellLocalZ - obsLocalZ;
                float r = ObstacleRadii[i] + halfCell;
                if (dx * dx + dz * dz < r * r)
                {
                    cost = byte.MaxValue;
                    break;
                }
            }

            CostField[index] = cost;
        }
    }

    /// <summary>
    /// BFS from the goal cell outward. Each cell receives the minimum number of steps to reach the goal.
    /// Cells with cost=255 (obstacles) are skipped and block propagation.
    /// Uses 4-directional expansion for uniform integration costs.
    /// </summary>
    [BurstCompile]
    private struct IntegrateFieldJob : IJob
    {
        public int GridWidth;
        public int GridHeight;
        public int GoalIndex;

        [ReadOnly] public NativeArray<byte> CostField;
        public NativeArray<ushort> IntegrationField;

        public void Execute()
        {
            int total = GridWidth * GridHeight;

            for (int i = 0; i < total; i++)
                IntegrationField[i] = ushort.MaxValue;

            IntegrationField[GoalIndex] = 0;

            var queue = new NativeQueue<int>(Allocator.Temp);
            queue.Enqueue(GoalIndex);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int cx = current % GridWidth;
                int cy = current / GridWidth;
                ushort currentCost = IntegrationField[current];

                // 4-directional BFS — manually unrolled for Burst compatibility
                TryEnqueue(cx - 1, cy, currentCost, ref queue);
                TryEnqueue(cx + 1, cy, currentCost, ref queue);
                TryEnqueue(cx, cy - 1, currentCost, ref queue);
                TryEnqueue(cx, cy + 1, currentCost, ref queue);
            }

            queue.Dispose();
        }

        private void TryEnqueue(int nx, int ny, ushort parentCost, ref NativeQueue<int> queue)
        {
            if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight)
                return;

            int nIndex = ny * GridWidth + nx;

            if (CostField[nIndex] == byte.MaxValue)
                return;

            ushort newCost = (ushort)(parentCost + 1);
            if (newCost < IntegrationField[nIndex])
            {
                IntegrationField[nIndex] = newCost;
                queue.Enqueue(nIndex);
            }
        }
    }

    /// <summary>
    /// For each cell, looks at 8 neighbors and picks the direction toward the one with the lowest integration cost.
    /// Blocked and unreachable cells fall back to pointing toward the grid center.
    /// Uses manually unrolled neighbor checks for Burst compatibility (no managed arrays or unsafe blocks).
    /// </summary>
    [BurstCompile]
    private struct BuildDirectionFieldJob : IJobParallelFor
    {
        public int GridWidth;
        public int GridHeight;
        public float3 GridRight;
        public float3 GridForward;

        [ReadOnly] public NativeArray<ushort> IntegrationField;

        [NativeDisableParallelForRestriction] public NativeArray<float3> DirectionField;

        public void Execute(int index)
        {
            int cx = index % GridWidth;
            int cy = index / GridWidth;
            ushort selfCost = IntegrationField[index];

            if (selfCost == 0)
            {
                DirectionField[index] = float3.zero;
                return;
            }

            ushort bestCost = selfCost;
            int bestDx = 0;
            int bestDy = 0;

            // 8-directional neighbor check — manually unrolled for Burst compatibility
            CheckNeighbor(cx, cy, -1, 0, selfCost, ref bestCost, ref bestDx, ref bestDy);
            CheckNeighbor(cx, cy, 1, 0, selfCost, ref bestCost, ref bestDx, ref bestDy);
            CheckNeighbor(cx, cy, 0, -1, selfCost, ref bestCost, ref bestDx, ref bestDy);
            CheckNeighbor(cx, cy, 0, 1, selfCost, ref bestCost, ref bestDx, ref bestDy);
            CheckNeighbor(cx, cy, -1, -1, selfCost, ref bestCost, ref bestDx, ref bestDy);
            CheckNeighbor(cx, cy, 1, -1, selfCost, ref bestCost, ref bestDx, ref bestDy);
            CheckNeighbor(cx, cy, -1, 1, selfCost, ref bestCost, ref bestDx, ref bestDy);
            CheckNeighbor(cx, cy, 1, 1, selfCost, ref bestCost, ref bestDx, ref bestDy);

            if (bestCost < selfCost)
            {
                float3 dir = GridRight * bestDx + GridForward * bestDy;
                DirectionField[index] = math.normalize(dir);
            }
            else
            {
                // Unreachable or all neighbors blocked — fallback toward grid center
                int halfW = GridWidth / 2;
                int halfH = GridHeight / 2;
                float3 toCenter = GridRight * (halfW - cx) + GridForward * (halfH - cy);
                DirectionField[index] = math.lengthsq(toCenter) > 0.001f
                    ? math.normalize(toCenter)
                    : float3.zero;
            }
        }

        private void CheckNeighbor(int cx, int cy, int dx, int dy, ushort selfCost,
            ref ushort bestCost, ref int bestDx, ref int bestDy)
        {
            int nx = cx + dx;
            int ny = cy + dy;
            if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight)
                return;

            ushort nCost = IntegrationField[ny * GridWidth + nx];
            if (nCost < bestCost)
            {
                bestCost = nCost;
                bestDx = dx;
                bestDy = dy;
            }
        }
    }

    /// <summary>
    /// Copies the computed direction and cost arrays into the FlowFieldCell DynamicBuffer.
    /// </summary>
    [BurstCompile]
    private struct CopyToCellBufferJob : IJob
    {
        [ReadOnly] public NativeArray<float3> DirectionField;
        [ReadOnly] public NativeArray<byte> CostField;

        public BufferLookup<FlowFieldCell> CellBufferLookup;
        public Entity FlowFieldEntity;

        public void Execute()
        {
            var buffer = CellBufferLookup[FlowFieldEntity];
            for (int i = 0; i < DirectionField.Length; i++)
            {
                buffer[i] = new FlowFieldCell
                {
                    Direction = DirectionField[i],
                    Cost = CostField[i]
                };
            }
        }
    }
}