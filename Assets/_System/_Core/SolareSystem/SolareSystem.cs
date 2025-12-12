using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace _System._Core
{
    public class SolareSystem
    {
        [SerializeField] private PlanetConfig _mainSolarRef; 
        [SerializeField] private readonly List<PlanetConfig> _els = new();

        [Serializable]
        public struct PlanetConfig
        {
            public GameObject planetRef;
            public float selfRotSpeed;
            public float orbitSpeed;
            public float orbitPlacement;
        }

        public void Awake()
        {
            // _mainSolarRef
        }

        public void Update()
        {
            _els.ForEach(planet =>
            {
                planet.planetRef.transform.RotateAround(Vector3.zero, Vector3.up, planet.orbitSpeed * Time.deltaTime);
                planet.planetRef.transform.Rotate(Vector3.up, planet.selfRotSpeed * Time.deltaTime);
            });
        }
    }
}