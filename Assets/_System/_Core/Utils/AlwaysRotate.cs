using UnityEngine;

namespace _System._Core.Utils
{
    public class AlwaysRotate : MonoBehaviour
    {
        [Tooltip("Axe de rotation (ex: Vector3.up pour tourner autour de l'axe Y).")]
        public Vector3 rotationAxis = Vector3.up;

        [Tooltip("Vitesse de rotation en degrés par seconde.")]
        public float rotationSpeed = 30f;

        private void Update()
        {
            transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime);
        }

        private void OnGUI()
        {
            transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime);
        }
    }
}