using System.Collections.Generic;
using UnityEngine;

namespace WFCContent.WFC
{
    public static class WFCUtils
    {
        public static readonly List<Vector3> DirOrder = new List<Vector3>()
        {
            Vector3.up,
            Vector3.right,
            Vector3.down,
            Vector3.left,
        };
        
        public static readonly List<Vector3Int> DirOrderInt = new List<Vector3Int>()
        {
            Vector3Int.up,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.left,
        };
    }
}