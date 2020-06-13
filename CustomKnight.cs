using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Ionic.Zip;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HollowKnight.CustomKnight
{
    public class CustomKnight : Mod, ITogglableMod
    {
        private const string CONFIG = "config.xml";
        private const string RESOURCE_DIR = "ResourcePacks";

        private static readonly string ManagedPath = Path.Combine(Application.dataPath,
            SystemInfo.operatingSystem.Contains("Mac") ? "Resources/Data/Managed" : "Managed");

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ResourcePack));
        private static readonly Dictionary<string, ResourcePack> Packs = new Dictionary<string, ResourcePack>();

        // Putting these in Entry marked [NonSerialized] kills the serializer for some reason
        private static readonly Dictionary<ResourcePack.Entry, Texture2D> Textures =
            new Dictionary<ResourcePack.Entry, Texture2D>();
        private static readonly Dictionary<ResourcePack.Entry, Sprite> Sprites =
            new Dictionary<ResourcePack.Entry, Sprite>();

        public static ResourcePack CurrentPack;

        public static Settings Settings { get; private set; } = new Settings();
        public override ModSettings GlobalSettings
        {
            get => Settings;
            set => Settings = value is Settings s ? s : Settings;
        }

        public override void Initialize()
        {
            GenerateExampleConfig();
            Packs.Clear();

            if (!Directory.Exists(GetFullPath(RESOURCE_DIR)))
            {
                Directory.CreateDirectory(GetFullPath(RESOURCE_DIR));
            }

            foreach (string packPath in Directory.GetFiles(GetFullPath(RESOURCE_DIR))
                .Where(fileName => fileName.ToLower().EndsWith(".zip")))
            {
                if (string.IsNullOrEmpty(packPath))
                {
                    continue;
                }

                try
                {
                    using ZipFile zip = ZipFile.Read(packPath);

                    ZipEntry configEntry = zip.Entries.FirstOrDefault(e => !e.IsDirectory && e.FileName == CONFIG);
                    if (configEntry == null)
                    {
                        throw new FileNotFoundException($"Resource pack must contain a config named '{CONFIG}'");
                    }

                    using Stream configStream = configEntry.OpenReader();
                    Packs.Add(packPath, (ResourcePack) Serializer.Deserialize(configStream));
                    Log($"Loaded pack '{Packs[packPath].Name}' with author '{Packs[packPath].Author}'");
                }
                catch (Exception e)
                {
                    LogError($"Failed to load resource pack '{Path.GetFileName(packPath)}'\n{e}");
                }
            }

            if (Packs.Count == 0)
            {
                return;
            }

            foreach (string packPath in Packs.Keys.ToArray())
            {
                foreach (ResourcePack.Entry entry in Packs[packPath].Entries)
                {
                    try
                    {
                        using ZipFile zip = ZipFile.Read(packPath);
                        ZipEntry texZip = zip.FirstOrDefault(zipEntry => zipEntry.FileName == entry.SpritePath);

                        if (texZip == null)
                        {
                            throw new FileNotFoundException($"Zip does not contain a file named '{entry.SpritePath}'");
                        }

                        using Stream texStream = texZip.OpenReader();
                        byte[] buffer = new byte[texStream.Length];
                        texStream.Read(buffer, 0, buffer.Length);

                        Textures[entry] = new Texture2D(1, 1);
                        Textures[entry].LoadImage(buffer, true);

                        Log($"Loaded texture '{entry.SpritePath}' from pack '{Packs[packPath].Name}'");

                        if (entry.Type != ResourcePack.Entry.SpriteType.SpriteRenderer)
                        {
                            continue;
                        }

                        Sprites[entry] = Sprite.Create
                        (
                            Textures[entry], new Rect(0, 0, Textures[entry].width, Textures[entry].height),
                            new Vector2(0.5f, 0.5f)
                        );
                    }
                    catch (Exception e)
                    {
                        LogError($"Failed to load texture '{entry.SpritePath}' from pack '{Packs[packPath].Name}'\n{e}");
                    }
                }
            }

            CurrentPack = Packs.Values.FirstOrDefault(pack => pack.Name == Settings.CurrentPack);
            Log($"Current resource pack is '{CurrentPack.Name}'");

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += EditSceneSprites;
        }

        public void Unload()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= EditSceneSprites;
        }

        private void EditSceneSprites(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Town")
            {
                foreach (SpriteRenderer spr in GameCameras.instance.hudCanvas.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (spr.name == "Pulse Sprite")
                    {
                        Transform t = spr.transform;
                        while (t != null)
                        {
                            Log(t.name);
                            t = t.parent;
                        }

                        break;
                    }
                }
            }

            if (CurrentPack.Entries == null)
            {
                return;
            }

            List<ResourcePack.Entry> entriesToCheck = new List<ResourcePack.Entry>();

            foreach (ResourcePack.Entry entry in CurrentPack.Entries)
            {
                if (scene.name == entry.SceneName ||
                    (entry.SceneUsesRegex && Regex.IsMatch(scene.name, entry.SceneName)))
                {
                    entriesToCheck.Add(entry);
                }
            }

            if (entriesToCheck.Count == 0)
            {
                return;
            }

            foreach (GameObject obj in Object.FindObjectsOfType<GameObject>()
                .Where(obj => obj.transform.parent == null))
            {
                foreach (ResourcePack.Entry entry in entriesToCheck)
                {
                    if (obj.name != entry.ObjectName &&
                        !(entry.ObjectUsesRegex && Regex.IsMatch(obj.name, entry.ObjectName)))
                    {
                        continue;
                    }

                    string[] path = entry.GameObjectPath.Split('/');
                    Transform t = obj.transform;
                    for (int i = 0;
                        i < (entry.Type == ResourcePack.Entry.SpriteType.tk2dSpriteAnimator
                            ? path.Length - 1
                            : path.Length);
                        i++)
                    {
                        t = t.Find(path[i]);
                        if (t == null)
                        {
                            break;
                        }
                    }

                    if (t == null)
                    {
                        continue;
                    }

                    try
                    {
                        switch (entry.Type)
                        {
                            case ResourcePack.Entry.SpriteType.tk2dSprite:
                                t.GetComponent<tk2dSprite>().GetCurrentSpriteDef().material.mainTexture =
                                    Textures[entry];
                                break;
                            case ResourcePack.Entry.SpriteType.tk2dSpriteAnimator:
                                t.GetComponent<tk2dSpriteAnimator>().GetClipByName(path[path.Length - 1])
                                        .frames[0].spriteCollection.spriteDefinitions[0].material.mainTexture =
                                    Textures[entry];
                                break;
                            case ResourcePack.Entry.SpriteType.SpriteRenderer:
                                t.GetComponent<SpriteRenderer>().sprite = Sprites[entry];
                                break;
                            default:
                                LogWarn($"Invalid sprite type '{entry.Type}' in pack '{CurrentPack.Name}'");
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        LogError($"Failed replacing sprite on object '{t.name}'\n{e}");
                    }
                }
            }
        }

        public void GenerateExampleConfig()
        {
            ResourcePack example = new ResourcePack
            {
                Author = "Your Name Here",
                Name = "Pack Name Here",
                Version = "1.0.0",
                Entries = new[]
                {
                    new ResourcePack.Entry
                    {
                        ObjectName = "Knight",
                        SceneName = "Knight_Pickup",
                        SceneUsesRegex = true,
                        SpritePath = "Images/Knight/Main.png",
                        GameObjectPath = "Idle",
                        Type = ResourcePack.Entry.SpriteType.tk2dSpriteAnimator
                    }
                }
            };

            using FileStream stream = File.Open(GetFullPath(RESOURCE_DIR + "/example.xml"), FileMode.Create);
            Serializer.Serialize(stream, example);
        }
        
        public static string GetFullPath(string path) => Path.Combine(ManagedPath, path);
    }
}