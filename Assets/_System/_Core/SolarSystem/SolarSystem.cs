using UnityEngine;
using System.Collections.Generic;

namespace _System._Core
{
    public class SolarSystem : MonoBehaviour
    {
        [SerializeField] private PlanetConfig _mainSolarRef;
        [SerializeField] private GameObject OutlineFeedback; // oe je sais pas nomer mes variables brr brr patapims
        [SerializeField] private List<PlanetConfig> _els = new();
        private Transform _lt;

        [System.Serializable]
        public class PlanetConfig // Changé en class pour faciliter la modification
        {
            public GameObject planetRef;
            public float selfRotSpeed;
            public float orbitPos;
            public float orbitDist;
            public float orbitSpeed;
        }

        public void Awake()
        {
            _lt = _mainSolarRef.planetRef.transform;

            foreach (var planet in _els)
            {
                Vector3 offset = new Vector3(
                    Mathf.Sin(planet.orbitPos) * planet.orbitDist,
                    0f,
                    Mathf.Cos(planet.orbitPos) * planet.orbitDist
                );

                var inst = Instantiate(OutlineFeedback);
                inst.transform.localScale = Vector3.one * (planet.orbitDist * 60f);
                
                planet.planetRef.transform.position = _lt.position + offset;
            }
        }

        public void Update()
        {
            foreach (var planet in _els)
            {
                planet.orbitPos += planet.orbitSpeed * Time.deltaTime;

                Vector3 offset = new Vector3(
                    Mathf.Sin(planet.orbitPos) * planet.orbitDist,
                    0f,
                    Mathf.Cos(planet.orbitPos) * planet.orbitDist
                );
                planet.planetRef.transform.position = _lt.position + offset;
 
                planet.planetRef.transform.Rotate(
                    Vector3.up,
                    planet.selfRotSpeed * Time.deltaTime
                );
            }
        }
    }
}
