using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// One field of every Unity built-in struct a save is likely to hold.
    /// </summary>
    /// <remarks>
    /// These types carry no <c>[System.Serializable]</c> - Unity handles them in native code instead
    /// - so a rule that asks for the attribute rejects all of them, and a save quietly loses its
    /// positions and colours. This fixture is what the parity test compares against Unity's own
    /// serializer, so the list is settled by what Unity does rather than by what anyone remembers.
    /// </remarks>
    [SaveId("built_in_structs")]
    public class BuiltInStructsSaveData : SaveData
    {
        public Vector2 Vector2;
        public Vector3 Vector3;
        public Vector4 Vector4;
        public Vector2Int Vector2Int;
        public Vector3Int Vector3Int;
        public Quaternion Quaternion;
        public Color Color;
        public Color32 Color32;
        public Rect Rect;
        public RectInt RectInt;
        public Bounds Bounds;
        public BoundsInt BoundsInt;
        public LayerMask LayerMask;

        public override void ResetToDefaults()
        {
            Vector2 = default;
            Vector3 = default;
            Vector4 = default;
            Vector2Int = default;
            Vector3Int = default;
            Quaternion = default;
            Color = default;
            Color32 = default;
            Rect = default;
            RectInt = default;
            Bounds = default;
            BoundsInt = default;
            LayerMask = default;
        }
    }
}
