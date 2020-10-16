using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using System.IO;
using Aseprite;
using UnityEditor;
using UnityEditor.AssetImporters;
using Aseprite.Chunks;
using System.Text;

namespace AsepriteImporter
{
    public enum AseFileImportType
    {
        Sprite,
        Tileset,
        LayerToSprite
    }

    public enum EmptyTileBehaviour
    {
        Keep,
        Index,
        Remove
    }

    public enum AseEditorBindType
    {
        SpriteRenderer,
        UIImage
    }

    [ScriptedImporter(1, new []{ "ase", "aseprite" })]
    public class AseFileImporter : ScriptedImporter
    {
        [SerializeField] public AseFileTextureSettings textureSettings = new AseFileTextureSettings();
        [SerializeField] public AseFileAnimationSettings[] animationSettings;
        [SerializeField] public Texture2D atlas;
        [SerializeField] public AseFileImportType importType;
        [SerializeField] public AseEditorBindType bindType;
        [SerializeField] public EmptyTileBehaviour emptyTileBehaviour;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            name = GetFileName(ctx.assetPath);

            AseFile aseFile = ReadAseFile(ctx.assetPath);
            int frameCount = aseFile.Header.Frames;

            SpriteAtlasBuilder atlasBuilder = new SpriteAtlasBuilder(textureSettings, aseFile.Header.Width, aseFile.Header.Height);

            Texture2D[] frames = null;
            if (importType != AseFileImportType.LayerToSprite)
                frames = aseFile.GetFrames(textureSettings.ignoreBackground);
            else
                frames = aseFile.GetLayersAsFrames();

            SpriteImportData[] spriteImportData;

            //if (textureSettings.transparentMask)
            //{
            //    atlas = atlasBuilder.GenerateAtlas(frames, out spriteImportData, textureSettings.transparentColor, false);
            //}
            //else
            //{
            //    atlas = atlasBuilder.GenerateAtlas(frames, out spriteImportData, false);

            //}

            atlas = atlasBuilder.GenerateAtlas(frames, name, out spriteImportData, textureSettings.transparencyMode, false);

            atlas.filterMode = textureSettings.filterMode;
            atlas.alphaIsTransparency = textureSettings.transparencyMode == TransparencyMode.Alpha;
            atlas.wrapMode = textureSettings.wrapMode;
            atlas.name = "Texture";

            MetaData[] metadatas = aseFile.GetMetaData(textureSettings.spritePivot, textureSettings.pixelsPerUnit);

            List<SecondarySpriteTexture> secondarySpriteTextures = new List<SecondarySpriteTexture>();
            
            // Find any metadata with a secondary texture
            foreach (var metadata in metadatas)
            {
                if (metadata.Type == MetaDataType.SECONDARY_TEXTURE)
                {
                    var textureName = metadata.Args[0];
                    
                    var secondaryTextureFrames = aseFile.GetLayerTexture(metadata.LayerIndex, metadata.Layer);
                    Texture2D secondaryTexture = atlasBuilder.GenerateAtlas(secondaryTextureFrames.ToArray(), name, out _,
                        textureSettings.transparencyMode, false);
                    
                    secondaryTexture.alphaIsTransparency = textureSettings.transparencyMode == TransparencyMode.Alpha;;
                    secondaryTexture.wrapMode = textureSettings.wrapMode;
                    secondaryTexture.filterMode = textureSettings.filterMode;
                    secondaryTexture.name = textureName;
                    
                    secondarySpriteTextures.Add(new SecondarySpriteTexture() {name = textureName, texture = secondaryTexture});
                }
            }
            
            TextureGenerationSettings generationSettings = new TextureGenerationSettings();
            
            generationSettings.platformSettings = new TextureImporterPlatformSettings();

            generationSettings.spriteImportData = spriteImportData;
            generationSettings.secondarySpriteTextures = secondarySpriteTextures.ToArray();
            generationSettings.sourceTextureInformation = new SourceTextureInformation()
            {
                width = atlas.width,
                height = atlas.height,
                containsAlpha = true,
            };

            generationSettings.textureImporterSettings = new TextureImporterSettings
            {
                textureType = TextureImporterType.Sprite,
                textureShape = TextureImporterShape.Texture2D,
                alphaIsTransparency = textureSettings.transparencyMode == TransparencyMode.Alpha,
                alphaSource = TextureImporterAlphaSource.FromInput,
                spritePixelsPerUnit = textureSettings.pixelsPerUnit,
                spritePivot = textureSettings.spritePivot,
                spriteMeshType = SpriteMeshType.Tight,
                convertToNormalMap = false,
                mipmapEnabled = false,
                sRGBTexture = true,
                readable = false,
                fadeOut = false,
                wrapMode = TextureWrapMode.Clamp,
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Clamp,
                wrapModeW = TextureWrapMode.Clamp
            };

            var imageBuffer = atlas.GetRawTextureData<Color32>();
            var generatedOutput = TextureGenerator.GenerateTexture(generationSettings, imageBuffer);
            generatedOutput.texture.name = "Texture";

            // Main texture atlas
            ctx.AddObjectToAsset("Texture", generatedOutput.texture);
            ctx.SetMainObject(generatedOutput.texture);
            
            // Add secondary textures
            foreach (var secondarySpriteTexture in secondarySpriteTextures)
            {
                ctx.AddObjectToAsset("SecondaryTex_" + secondarySpriteTexture.name, secondarySpriteTexture.texture);
            }

            // Add sprites
            foreach (var sprite in generatedOutput.sprites)
            {
                ctx.AddObjectToAsset(sprite.name, sprite);
            }
            
            // Add animations
            if (importType == AseFileImportType.Sprite)
            {
                GenerateAnimations(ctx, aseFile, generatedOutput.sprites, metadatas);
            }

            switch (importType)
            {
                case AseFileImportType.LayerToSprite:
                case AseFileImportType.Sprite:
                    // ImportSprites(ctx, aseFile, spriteImportData, metadatas);
                    break;
                case AseFileImportType.Tileset:
                    ImportTileset(ctx, atlas);
                    break;
            }
        }

        private void ImportSprites(AssetImportContext ctx, AseFile aseFile, SpriteImportData[] spriteImportData, MetaData[] metadatas)
        {
            int spriteCount = spriteImportData.Length;


            Sprite[] sprites = new Sprite[spriteCount];

            for (int i = 0; i < spriteCount; i++)
            {
                Sprite sprite = Sprite.Create(atlas,
                    spriteImportData[i].rect,
                    spriteImportData[i].pivot, textureSettings.pixelsPerUnit, textureSettings.extrudeEdges,
                    textureSettings.meshType, spriteImportData[i].border, textureSettings.generatePhysics);
                sprite.name = string.Format("{0}_{1}", name, spriteImportData[i].name);

                ctx.AddObjectToAsset(sprite.name, sprite);
                sprites[i] = sprite;
            }

            GenerateAnimations(ctx, aseFile, sprites, metadatas);
        }

        private void ImportTileset(AssetImportContext ctx, Texture2D atlas)
        {
            int cols = atlas.width / textureSettings.tileSize.x;
            int rows = atlas.height / textureSettings.tileSize.y;

            int width = textureSettings.tileSize.x;
            int height = textureSettings.tileSize.y;

            int index = 0;

            for (int y = rows - 1; y >= 0; y--)
            {
                for (int x = 0; x < cols; x++)
                {
                    Rect tileRect = new Rect(x * width, y * height, width, height);

                    if (emptyTileBehaviour != EmptyTileBehaviour.Keep)
                    {
                        if (IsTileEmpty(tileRect, atlas))
                        {
                            if (emptyTileBehaviour == EmptyTileBehaviour.Index) index++;
                            continue;
                        }
                    }

                    Sprite sprite = Sprite.Create(atlas, tileRect, textureSettings.spritePivot,
                        textureSettings.pixelsPerUnit, textureSettings.extrudeEdges, textureSettings.meshType,
                        Vector4.zero, textureSettings.generatePhysics);
                    sprite.name = string.Format("{0}_{1}", name, index);

                    ctx.AddObjectToAsset(sprite.name, sprite);

                    index++;
                }
            }
        }

        private bool IsTileEmpty(Rect tileRect, Texture2D atlas)
        {
            Color[] tilePixels = atlas.GetPixels((int)tileRect.xMin, (int)tileRect.yMin, (int)tileRect.width, (int)tileRect.height);
            for (int i = 0; i < tilePixels.Length; i++)
            {
                if (tilePixels[i].a != 0) 
                    return false;
            }
            return true;
        }

        private string GetFileName(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string filename = parts[parts.Length - 1];

            return filename.Substring(0, filename.LastIndexOf('.'));
        }

        private static AseFile ReadAseFile(string assetPath)
        {
            FileStream fileStream = new FileStream(assetPath, FileMode.Open, FileAccess.Read);
            AseFile aseFile = new AseFile(fileStream);
            fileStream.Close();

            return aseFile;
        }

        private void GenerateAnimations(AssetImportContext ctx, AseFile aseFile, Sprite[] sprites, MetaData[] metadatas)
        {
            if (animationSettings == null)
                animationSettings = new AseFileAnimationSettings[0];

            var animSettings = new List<AseFileAnimationSettings>(animationSettings);
            var animations = aseFile.GetAnimations();

            if (animations.Length <= 0)
                return;


            if (animationSettings != null)
                RemoveUnusedAnimationSettings(animSettings, animations);

            int index = 0;

            foreach (var animation in animations)
            {
                AnimationClip animationClip = new AnimationClip();
                animationClip.name = name + "_" + animation.TagName;
                animationClip.frameRate = 25;

                AseFileAnimationSettings importSettings = GetAnimationSettingFor(animSettings, animation);
                importSettings.about = GetAnimationAbout(animation);

                EditorCurveBinding editorBinding = new EditorCurveBinding();
                editorBinding.path = "";
                editorBinding.propertyName = "m_Sprite";

                switch (bindType)
                {
                    case AseEditorBindType.SpriteRenderer:
                        editorBinding.type = typeof(SpriteRenderer);
                        break;
                    case AseEditorBindType.UIImage:
                        editorBinding.type = typeof(Image);
                        break;
                }


                int length = animation.FrameTo - animation.FrameFrom + 1;
                ObjectReferenceKeyframe[] spriteKeyFrames = new ObjectReferenceKeyframe[length + 1]; // plus last frame to keep the duration
                Dictionary<string, AnimationCurve> transformCurveX = new Dictionary<string, AnimationCurve>(),
                                                   transformCurveY = new Dictionary<string, AnimationCurve>();

                float time = 0;

                int from = (animation.Animation != LoopAnimation.Reverse) ? animation.FrameFrom : animation.FrameTo;
                int step = (animation.Animation != LoopAnimation.Reverse) ? 1 : -1;

                int keyIndex = from;

                for (int i = 0; i < length; i++)
                {
                    if (i >= length)
                    {
                        keyIndex = from;
                    }


                    ObjectReferenceKeyframe frame = new ObjectReferenceKeyframe();
                    frame.time = time;
                    frame.value = sprites[keyIndex];

                    time += aseFile.Frames[keyIndex].FrameDuration / 1000f;
                    spriteKeyFrames[i] = frame;

                    foreach(var metadata in metadatas)
                        if(metadata.Type == MetaDataType.TRANSFORM && metadata.Transforms.ContainsKey(keyIndex))
                        {
                            var childTransform = metadata.Args[0];
                            if (!transformCurveX.ContainsKey(childTransform))
                            {
                                transformCurveX[childTransform] = new AnimationCurve();
                                transformCurveY[childTransform] = new AnimationCurve();
                            }
                            var pos = metadata.Transforms[keyIndex];
                            transformCurveX[childTransform].AddKey(i, pos.x);
                            transformCurveY[childTransform].AddKey(i, pos.y);
                        }

                    keyIndex += step;
                }

                float frameTime = 1f / animationClip.frameRate;

                ObjectReferenceKeyframe lastFrame = new ObjectReferenceKeyframe();
                lastFrame.time = time - frameTime;
                lastFrame.value = sprites[keyIndex - step];

                spriteKeyFrames[spriteKeyFrames.Length - 1] = lastFrame;
                foreach (var metadata in metadatas)
                    if (metadata.Type == MetaDataType.TRANSFORM && metadata.Transforms.ContainsKey(keyIndex - step))
                    {
                        var childTransform = metadata.Args[0];
                        var pos = metadata.Transforms[keyIndex - step];
                        transformCurveX[childTransform].AddKey(spriteKeyFrames.Length - 1, pos.x);
                        transformCurveY[childTransform].AddKey(spriteKeyFrames.Length - 1, pos.y);
                    }

                AnimationUtility.SetObjectReferenceCurve(animationClip, editorBinding, spriteKeyFrames);

                foreach (var childTransform in transformCurveX.Keys)
                {
                    EditorCurveBinding
                    bindingX = new EditorCurveBinding { path = childTransform, type = typeof(Transform), propertyName = "m_LocalPosition.x" },
                    bindingY = new EditorCurveBinding { path = childTransform, type = typeof(Transform), propertyName = "m_LocalPosition.y" };
                    MakeConstant(transformCurveX[childTransform]);
                    AnimationUtility.SetEditorCurve(animationClip, bindingX, transformCurveX[childTransform]);
                    MakeConstant(transformCurveY[childTransform]);
                    AnimationUtility.SetEditorCurve(animationClip, bindingY, transformCurveY[childTransform]);
                }

                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(animationClip);

                switch (animation.Animation)
                {
                    case LoopAnimation.Forward:
                        animationClip.wrapMode = WrapMode.Loop;
                        settings.loopTime = true;
                        break;
                    case LoopAnimation.Reverse:
                        animationClip.wrapMode = WrapMode.Loop;
                        settings.loopTime = true;
                        break;
                    case LoopAnimation.PingPong:
                        animationClip.wrapMode = WrapMode.PingPong;
                        settings.loopTime = true;
                        break;
                }

                if (!importSettings.loopTime)
                {
                    animationClip.wrapMode = WrapMode.Once;
                    settings.loopTime = false;
                }

                AnimationUtility.SetAnimationClipSettings(animationClip, settings);
                ctx.AddObjectToAsset(animation.TagName, animationClip);

                index++;
            }

            animationSettings = animSettings.ToArray();
        }

        private void RemoveUnusedAnimationSettings(List<AseFileAnimationSettings> animationSettings,
            FrameTag[] animations)
        {
            for (int i = 0; i < animationSettings.Count; i++)
            {
                bool found = false;
                if (animationSettings[i] != null)
                {
                    foreach (var anim in animations)
                    {
                        if (animationSettings[i].animationName == anim.TagName)
                            found = true;
                    }
                }

                if (!found)
                {
                    animationSettings.RemoveAt(i);
                    i--;
                }
            }
        }

        public AseFileAnimationSettings GetAnimationSettingFor(List<AseFileAnimationSettings> animationSettings,
            FrameTag animation)
        {
            if (animationSettings == null)
                animationSettings = new List<AseFileAnimationSettings>();

            for (int i = 0; i < animationSettings.Count; i++)
            {
                if (animationSettings[i].animationName == animation.TagName)
                    return animationSettings[i];
            }

            animationSettings.Add(new AseFileAnimationSettings(animation.TagName));
            return animationSettings[animationSettings.Count - 1];
        }

        private string GetAnimationAbout(FrameTag animation)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Animation Type:\t{0}", animation.Animation.ToString());
            sb.AppendLine();
            sb.AppendFormat("Animation:\tFrom: {0}; To: {1}", animation.FrameFrom, animation.FrameTo);

            return sb.ToString();
        }

        static void MakeConstant(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; ++i)
            {
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
        }
    }
}
