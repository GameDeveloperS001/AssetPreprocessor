﻿using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AssetPreprocessor.Scripts.Editor
{
    class TexturePreprocessor : AssetPostprocessor
    {
        /// <summary>
        /// https://docs.unity3d.com/ScriptReference/AssetPostprocessor.OnPreprocessTexture.html
        /// </summary>
        private void OnPreprocessTexture()
        {
            var textureImporter = (TextureImporter) assetImporter;

            var assetPath = textureImporter.assetPath;
            var textureName = AssetPreprocessorUtils.GetAssetNameFromPath(textureImporter.assetPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            var platformName = EditorUserBuildSettings.activeBuildTarget.ToString();
			
            var configs = AssetPreprocessorUtils.GetScriptableObjectsOfType<TexturePreprocessorConfig>();
            
            if (configs.Count == 0)
            {
                Debug.Log($"No existing {nameof(TexturePreprocessorConfig)} found.");
                
                return;
            }
            
            configs = configs
                .Where(conf => conf.ShouldUseConfigForAssetImporter(assetImporter))
                .ToList();
            
            configs.Sort((config1, config2) => config1.ConfigSortOrder.CompareTo(config2.ConfigSortOrder));

            TexturePreprocessorConfig config = null;

            for (var i = 0; i < configs.Count; i++)
            {
                var configToTest = configs[i];
		            
                if (!AssetPreprocessorUtils.DoesRegexStringListMatchString(configToTest.PlatformsRegexList, platformName)) continue;
                
                // Found matching config.
                config = configToTest;
                
                break;
            }
            
            // If could not find a matching config, don't process the texture.
            if (config == null) return;

            var currentPlatform = EditorUserBuildSettings.activeBuildTarget.ToString();
            var currentPlatformSettings = textureImporter.GetPlatformTextureSettings(currentPlatform);
			
            var hasAlpha = textureImporter.DoesSourceTextureHaveAlpha();
            var nativeTextureSize = GetOriginalTextureSize(textureImporter);
            var nativeSize = Mathf.NextPowerOfTwo(Mathf.Max(nativeTextureSize.width, nativeTextureSize.height));
            var currentFormat = currentPlatformSettings.format.ToString();
            
            Debug.Log($"Processing: {textureName} | Native size: {nativeSize} | Current format: {currentFormat}", texture);
            Debug.Log($"Using: {config.name}", config);
            
            // If already contains correct texture format, skip adjusting import settings.
            var matchingSkipRegex = config.SkipIfCurrentTextureFormatContains.Find(regexString => new Regex(regexString).IsMatch(currentFormat));
            var alreadyContainsFormat = matchingSkipRegex != null;
            if (!config.ForcePreprocess && alreadyContainsFormat)
            {
                Debug.Log($"Skipping preprocess. Current format matching skip regex: '{matchingSkipRegex}'", texture);
                return;
            }
			
            if (config.EnableReadWrite && !textureImporter.isReadable)
            {
                Debug.Log("Enabling Read/Write.", texture);
                textureImporter.isReadable = true;
            }
			
            var maxTextureSize = config.MaxTextureSize;
            var multipliedNativeRes = Mathf.RoundToInt(nativeSize * config.NativeResMultiplier);
            var textureSize = Mathf.Min(multipliedNativeRes, maxTextureSize);
			
            var format = hasAlpha ? config.RGBAFormat : config.RGBFormat;

            if (config.ForceLinear)
            {
                textureImporter.sRGBTexture = false;
                Debug.Log("Forcing linear.", texture);
            }

            SetTextureImporterPlatformSetting(config, textureImporter, texture, textureName, textureSize, format);
        }

        private static void SetTextureImporterPlatformSetting(
            TexturePreprocessorConfig config,
            TextureImporter textureImporter,
            Texture texture,
            string textureName,
            int textureSize,
            TextureImporterFormat format
        )
        {
            Debug.Log($"Setting: {textureSize} | Format: {format} | {textureName}", texture);

            config.PlatformsRegexList.ForEach(platformName =>
            {
                textureImporter.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    overridden = true,
                    name = platformName,
                    maxTextureSize = textureSize,
                    format = format,
                    compressionQuality = (int) config.TextureCompressionQuality,
                    allowsAlphaSplitting = false
                });
            });

            textureImporter.npotScale = config.NPOTScale;
        }

        /// <summary>
        /// Hacky way to get the native texture size via the TextureImporter.
        /// https://forum.unity.com/threads/getting-original-size-of-texture-asset-in-pixels.165295/
        /// </summary>
        private static Size GetOriginalTextureSize(TextureImporter importer)
        {
            if (_getImageSizeDelegate == null) {
                var method = typeof(TextureImporter).GetMethod("GetWidthAndHeight", BindingFlags.NonPublic | BindingFlags.Instance);
                _getImageSizeDelegate = Delegate.CreateDelegate(typeof(GetImageSize), null, method) as GetImageSize;
            }
 
            var size = new Size();
            
            _getImageSizeDelegate(importer, ref size.width, ref size.height);
 
            return size;
        }
		
        private delegate void GetImageSize(TextureImporter importer, ref int width, ref int height);
        private static GetImageSize _getImageSizeDelegate;

        private struct Size {
            public int width;
            public int height;
        }
    }
}
