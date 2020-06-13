using System;

namespace HollowKnight.CustomKnight
{
    [Serializable]
    public struct ResourcePack
    {
        public string Name;
        public string Author;
        public string Version;

        public Entry[] Entries;

        [Serializable]
        public struct Entry
        {
            public string SceneName;
            public bool SceneUsesRegex;
            public string ObjectName;
            public bool ObjectUsesRegex;
            public string SpritePath;
            public string GameObjectPath;
            public SpriteType Type;

            public enum SpriteType
            {
                // ReSharper disable InconsistentNaming
                tk2dSprite,
                tk2dSpriteAnimator,
                SpriteRenderer
            }
        }
    }
}
