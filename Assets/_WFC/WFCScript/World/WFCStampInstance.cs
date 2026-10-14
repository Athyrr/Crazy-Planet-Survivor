using UnityEngine;
using WFCContent.WFC;

namespace WFCContent.World
{
    // pose par GoldbergPolyhedron sur chaque stamp instancie: le slot qu'il occupe et la piece posee.
    // la planete retrouve ses stamps par ce composant (re-resolution locale de la peinture, nettoyage des stamps perdus)
    [AddComponentMenu(""), DisallowMultipleComponent]
    public class WFCStampInstance : MonoBehaviour
    {
        public int slot;
        public WFCSlotKey key;
    }
}
