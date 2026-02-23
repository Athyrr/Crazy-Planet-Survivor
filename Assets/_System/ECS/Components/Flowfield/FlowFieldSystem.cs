using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))] // Idéal pour la logique de mouvement/pathfinding
[BurstCompile]
public partial struct FlowFieldSystem : ISystem
{
    // On stocke les résultats pour que d'autres systèmes (ex: Avoidance) puissent s'en servir
    private NativeArray<int> _integrationField;
    private NativeArray<float3> _vectorField;
    private float3 _lastTargetPos;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // On requiert le composant qui contient la référence au Blob, et non le Blob lui-même
        state.RequireForUpdate<FlowFieldDatabase>();
        state.RequireForUpdate<Player>(); // En supposant que ton Player serve de cible
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        // Nettoyage de la mémoire non gérée
        if (_integrationField.IsCreated) _integrationField.Dispose();
        if (_vectorField.IsCreated) _vectorField.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var database = SystemAPI.GetSingleton<FlowFieldDatabase>();
        if (!database.Blobs.IsCreated) return;

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var playerPos = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

        int vertexCount = database.Blobs.Value.VertexCount;

        // Initialisation ou redimensionnement des tableaux natifs si nécessaire
        if (!_integrationField.IsCreated || _integrationField.Length != vertexCount)
        {
            if (_integrationField.IsCreated) _integrationField.Dispose();
            if (_vectorField.IsCreated) _vectorField.Dispose();
            
            _integrationField = new NativeArray<int>(vertexCount, Allocator.Persistent);
            _vectorField = new NativeArray<float3>(vertexCount, Allocator.Persistent);
        }

        // Optimisation : On ne recalcule que si le joueur a bougé de façon significative
        if (math.distancesq(_lastTargetPos, playerPos) < 1f) return;
        _lastTargetPos = playerPos;

        // Le BFS est difficile à paralléliser efficacement, on utilise donc un IJob standard (Mono-thread).
        // Mais grâce à Burst et aux pointeurs natifs du Blob, cela s'exécutera en quelques millisecondes.
        var job = new CalculateFlowFieldJob
        {
            Blob = database.Blobs,
            TargetPos = playerPos,
            IntegrationField = _integrationField,
            VectorField = _vectorField
        };

        state.Dependency = job.Schedule(state.Dependency);
    }
}

[BurstCompile]
public struct CalculateFlowFieldJob : IJob
{
    [ReadOnly] public BlobAssetReference<FlowFieldBlob> Blob;
    public float3 TargetPos;
    
    public NativeArray<int> IntegrationField;
    public NativeArray<float3> VectorField;

    public void Execute()
    {
        ref var blob = ref Blob.Value;
        int vertexCount = blob.VertexCount;

        // --- PHASE 1 : Trouver le nœud cible (le plus proche du joueur) ---
        int startNode = 0;
        float minDistSq = float.MaxValue;
        
        for (int i = 0; i < vertexCount; i++)
        {
            IntegrationField[i] = int.MaxValue; // Reset du champ
            VectorField[i] = float3.zero;

            float distSq = math.distancesq(blob.Positions[i], TargetPos);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                startNode = i;
            }
        }

        // --- PHASE 2 : BFS (Integration Field) ---
        // On propage un coût croissant à partir de la cible vers tous les voisins accessibles
        var queue = new NativeQueue<int>(Allocator.Temp);
        queue.Enqueue(startNode);
        IntegrationField[startNode] = 0;

        while (queue.TryDequeue(out int current))
        {
            int currentCost = IntegrationField[current];
            int offset = blob.NeighborOffsets[current];
            int count = blob.NeighborCounts[current];

            for (int i = 0; i < count; i++)
            {
                int neighbor = blob.Neighbors[offset + i];
                int neighborCost = currentCost + 1; // Coût uniforme de +1 par nœud

                // Si on trouve un chemin plus court vers ce voisin, on le met à jour
                if (neighborCost < IntegrationField[neighbor])
                {
                    IntegrationField[neighbor] = neighborCost;
                    queue.Enqueue(neighbor);
                }
            }
        }
        queue.Dispose();

        // --- PHASE 3 : Vector Field (Directions) ---
        // Chaque nœud regarde ses voisins et pointe vers celui qui a le coût le plus bas
        for (int i = 0; i < vertexCount; i++)
        {
            if (IntegrationField[i] == 0) continue; // Le nœud cible reste à 0

            int bestNeighbor = -1;
            int bestCost = IntegrationField[i]; 

            int offset = blob.NeighborOffsets[i];
            int count = blob.NeighborCounts[i];

            for (int j = 0; j < count; j++)
            {
                int neighbor = blob.Neighbors[offset + j];
                if (IntegrationField[neighbor] < bestCost)
                {
                    bestCost = IntegrationField[neighbor];
                    bestNeighbor = neighbor;
                }
            }

            // On génère le vecteur de direction normalisé vers le meilleur voisin
            if (bestNeighbor != -1)
            {
                VectorField[i] = math.normalize(blob.Positions[bestNeighbor] - blob.Positions[i]);
            }
        }
    }
}