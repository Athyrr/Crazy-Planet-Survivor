using UnityEngine;

public class BATAnimGraph : MonoBehaviour
{
    [Header("Animation Data")]
    public BATAnimationData[] animations;

    [Header("Settings")]
    [Range(0f, 1f)]
    public float animationSelector = 0f;
    public float globalSpeed = 1f;
    public bool autoPlay = true;

    [Header("Crossfade")]
    public float crossfadeDuration = 0.25f;
    public bool enableCrossfade = true;

    [Header("Renderer")]
    public MeshRenderer meshRenderer;
    public int materialIndex = 0;

    private int _currentIndex = -1;
    private float _crossfadeTimer;
    private float _localTime;

    private MaterialPropertyBlock _mpb;

    private void Start()
    {
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer == null || animations == null || animations.Length == 0) return;

        _mpb = new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(_mpb, materialIndex);

        if (animations[0] != null && animations[0].IsValid())
        {
            ApplyAnimationToBlock(animations[0], false);
            _mpb.SetFloat("_BATManualTime", 1f);
            _currentIndex = 0;
        }
    }

    private void Update()
    {
        if (meshRenderer == null || animations == null || animations.Length == 0) return;

        int targetIndex = GetTargetIndex();

        if (targetIndex != _currentIndex && enableCrossfade && crossfadeDuration > 0f)
        {
            StartCrossfade(targetIndex);
        }
        else if (targetIndex != _currentIndex)
        {
            SnapToAnimation(targetIndex);
        }

        AdvanceTime();

        if (_crossfadeTimer > 0f)
            UpdateCrossfade();
        else
            UpdateSingleAnimation();

        meshRenderer.SetPropertyBlock(_mpb, materialIndex);
    }

    private int GetTargetIndex()
    {
        if (animations.Length <= 1) return 0;
        return Mathf.Clamp(Mathf.RoundToInt(animationSelector * (animations.Length - 1)), 0, animations.Length - 1);
    }

    private void ApplyAnimationToBlock(BATAnimationData anim, bool isBlend)
    {
        if (isBlend)
        {
            _mpb.SetTexture("_BATMap2", anim.batMap);
            _mpb.SetFloat("_BATFrames2", anim.frames);
            _mpb.SetFloat("_BATFrameRows2", anim.frameRows);
            _mpb.SetFloat("_BATPlaneHeight2", anim.planeHeight);
            _mpb.SetFloat("_BAT_FPS_Blend", anim.fps);
            _mpb.SetFloat("_BATBlendFrame", 0f);
        }
        else
        {
            _mpb.SetTexture("_BATMap", anim.batMap);
            _mpb.SetFloat("_BATBoneCount", anim.boneCount);
            _mpb.SetFloat("_BATColumns", anim.columns);
            _mpb.SetFloat("_BATFrameRows", anim.frameRows);
            _mpb.SetFloat("_BATFrames", anim.frames);
            _mpb.SetFloat("_BATPlaneWidth", anim.planeWidth);
            _mpb.SetFloat("_BATPlaneHeight", anim.planeHeight);
            _mpb.SetFloat("_BATPlaneGridX", anim.planeGridX);
            _mpb.SetFloat("_BATPlaneGridY", anim.planeGridY);
            _mpb.SetFloat("_BAT_FPS", anim.fps);
            _mpb.SetFloat("_BATFrame", 0f);
        }

        _mpb.SetFloat("_BATManualTime", 1f);
    }

    private void SnapToAnimation(int index)
    {
        if (index < 0 || index >= animations.Length) return;
        var anim = animations[index];
        if (anim == null || !anim.IsValid()) return;

        _currentIndex = index;
        _crossfadeTimer = 0f;
        _localTime = 0f;

        ApplyAnimationToBlock(anim, false);
        _mpb.SetFloat("_BATBlend", 0f);
    }

    private void StartCrossfade(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= animations.Length) return;
        var next = animations[targetIndex];
        if (next == null || !next.IsValid()) return;

        if (_currentIndex >= 0 && _currentIndex < animations.Length)
        {
            var current = animations[_currentIndex];
            if (current != null && current.IsValid())
                ApplyAnimationToBlock(current, false);
        }

        _currentIndex = targetIndex;
        _crossfadeTimer = crossfadeDuration;

        ApplyAnimationToBlock(next, true);
        _mpb.SetFloat("_BATBlend", 0f);
    }

    private void AdvanceTime()
    {
        if (autoPlay)
            _localTime += Time.deltaTime * globalSpeed;
    }

    private void UpdateCrossfade()
    {
        _crossfadeTimer -= Time.deltaTime;
        float blend = Mathf.Clamp01(1f - (_crossfadeTimer / crossfadeDuration));

        if (_crossfadeTimer <= 0f)
        {
            FinishCrossfade();
            return;
        }

        var next = animations[_currentIndex];
        float frameBlend = _localTime * next.fps;

        _mpb.SetFloat("_BATBlendFrame", frameBlend);
        _mpb.SetFloat("_BATBlend", blend);
    }

    private void FinishCrossfade()
    {
        var next = animations[_currentIndex];
        ApplyAnimationToBlock(next, false);
        _mpb.SetFloat("_BATBlend", 0f);
    }

    private void UpdateSingleAnimation()
    {
        if (_currentIndex < 0 || _currentIndex >= animations.Length) return;

        var anim = animations[_currentIndex];
        if (anim == null) return;

        float frame = _localTime * anim.fps;
        _mpb.SetFloat("_BATFrame", frame);
    }

    public void SetAnimationIndex(int index)
    {
        animationSelector = animations.Length > 1
            ? Mathf.Clamp01((float)index / (animations.Length - 1))
            : 0f;
    }

    public void SetAnimationNormalized(float normalized)
    {
        animationSelector = Mathf.Clamp01(normalized);
    }

    public void Play() { autoPlay = true; }
    public void Pause() { autoPlay = false; }
    public void ResetTime() { _localTime = 0f; }

    public BATAnimationData GetCurrentAnimation()
    {
        if (_currentIndex < 0 || _currentIndex >= animations.Length) return null;
        return animations[_currentIndex];
    }
}
