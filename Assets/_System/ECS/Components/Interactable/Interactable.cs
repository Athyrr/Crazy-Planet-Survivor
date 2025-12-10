using Unity.Entities;

public struct Interactable : IComponentData
{
    public EInteractionType InteractionType;

    public float Radius;
}

