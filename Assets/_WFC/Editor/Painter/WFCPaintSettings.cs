using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WFCContent.WFC;

namespace Editor
{
    public enum PaintTool
    {
        Relief,  // hauteur des sommets (etages)
        Stamp,   // pieces imposees
        Zone,    // regles d'une zone (foret, village...)
        Lock,    // gele la piece posee
        Reroll,  // nouveau tirage sous le pinceau
        Erase    // efface les marques
    }

    public enum BrushShape
    {
        Circle, // tout ce qui est sous le cercle
        Cell,   // un seul sommet / slot
        Fill    // pot de peinture: la region connexe identique
    }

    public enum ReliefOp
    {
        Paint,   // hauteur choisie
        Raise,   // +1 etage
        Lower,   // -1 etage
        Flatten, // hauteur du premier point du trait
        Smooth   // moyenne des voisins
    }

    // reglages du pinceau, propres a chaque utilisateur (UserSettings, pas dans le projet)
    [FilePath("UserSettings/WFCPaintSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class WFCPaintSettings : ScriptableSingleton<WFCPaintSettings>
    {
        public const float MinSize = 0.4f;
        public const float MaxSize = 12f;

        public PaintTool tool = PaintTool.Relief;
        public BrushShape shape = BrushShape.Circle;
        [Tooltip("Rayon du pinceau, en longueurs d'arete de la grille")]
        public float size = 1.5f;

        public ReliefOp reliefOp = ReliefOp.Paint;
        public int height = 1;
        [Tooltip("Imposer: le WFC doit respecter la hauteur. Guider: le WFC est seulement pousse vers elle")]
        public bool force = true;

        public List<WFCElementData> stamps = new();
        [Tooltip("Ne pose une piece que la ou elle colle au relief actuel (une foret ne recouvre pas la cote)")]
        public bool stampFitRelief = true;
        [Tooltip("-1 = le WFC choisit la rotation")]
        public int stampRotation = -1;

        public WFCPaintZone zone;

        public bool eraseRelief = true;
        public bool eraseStamps = true;
        public bool eraseZones = true;
        public bool eraseLocks = true;

        [Tooltip("Resout pendant le trait (sinon au relachement)")]
        public bool liveSolve = true;
        [Tooltip("Couronnes de slots re-resolues autour du trait (le reste de la planete ne bouge pas)")]
        public int margin = 2;

        public bool showRelief = true;
        public bool showStamps = true;
        public bool showZones = true;
        public bool showLocks = true;
        public bool showConflicts = true;
        [Tooltip("La peinture reste visible a travers le relief")]
        public bool xray = true;
        [Range(0.1f, 1f)] public float opacity = 0.65f;

        public void Changed()
        {
            size = Mathf.Clamp(size, MinSize, MaxSize);
            margin = Mathf.Clamp(margin, 1, 8);
            stamps.RemoveAll(s => s == null);
            Save(true);
            WFCPaintVisuals.Invalidate();
            SceneView.RepaintAll();
        }

        public bool Shows(WFCContent.World.WFCPlanetPainting.Layer layer) => layer switch
        {
            WFCContent.World.WFCPlanetPainting.Layer.Relief => showRelief,
            WFCContent.World.WFCPlanetPainting.Layer.Stamps => showStamps,
            WFCContent.World.WFCPlanetPainting.Layer.Zones => showZones,
            _ => showLocks
        };
    }
}
