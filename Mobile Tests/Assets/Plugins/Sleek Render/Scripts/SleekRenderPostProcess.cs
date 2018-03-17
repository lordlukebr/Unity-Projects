﻿using UnityEngine;
using UnityEngine.Rendering;

namespace SleekRender
{
    [AddComponentMenu("Effects/Sleek Render Post Process")]
    [RequireComponent(typeof(Camera))]
    [ExecuteInEditMode, DisallowMultipleComponent]
    public class SleekRenderPostProcess : MonoBehaviour
    {
        public static class Uniforms
        {
            public static readonly int _LuminanceConst = Shader.PropertyToID("_LuminanceConst");
            public static readonly int _BloomIntencity = Shader.PropertyToID("_BloomIntencity");
            public static readonly int _BloomTint = Shader.PropertyToID("_BloomTint");
            public static readonly int _MainTex = Shader.PropertyToID("_MainTex");
            public static readonly int _BloomTex = Shader.PropertyToID("_BloomTex");
            public static readonly int _PreComposeTex = Shader.PropertyToID("_PreComposeTex");
            public static readonly int _TexelSize = Shader.PropertyToID("_TexelSize");
            public static readonly int _Colorize = Shader.PropertyToID("_Colorize");
            public static readonly int _VignetteShape = Shader.PropertyToID("_VignetteShape");
            public static readonly int _VignetteColor = Shader.PropertyToID("_VignetteColor");
        }

        private static class Keywords
        {
            public const string COLORIZE_ON = "COLORIZE_ON";
            public const string BLOOM_ON = "BLOOM_ON";
            public const string VIGNETTE_ON = "VIGNETTE_ON";
        }

        public SleekRenderSettings settings;
        private Material _downsampleMaterial;
        private Material _horizontalBlurMaterial;
        private Material _verticalBlurMaterial;
        private Material _preComposeMaterial;
        private Material _composeMaterial;

        private RenderTexture _downsampledBrightpassTexture;
        private RenderTexture _brightPassBlurTexture;
        private RenderTexture _horizontalBlurTexture;
        private RenderTexture _verticalBlurTexture;
        private RenderTexture _preComposeTexture;

        private Camera _mainCamera;
        private Mesh _fullscreenQuadMesh;

        private int _currentCameraPixelWidth;
        private int _currentCameraPixelHeight;

        private bool _isColorizeAlreadyEnabled = false;
        private bool _isBloomAlreadyEnabled = false;
        private bool _isVignetteAlreadyEnabled = false;
        private bool _isAlreadyPreservingAspectRatio = false;

        private void OnEnable()
        {
            CreateDefaultSettingsIfNoneLinked();
            CreateResources();
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture target)
        {
#if UNITY_EDITOR
            CheckScreenSizeAndRecreateTexturesIfNeeded(_mainCamera);
#endif

            ApplyPostProcess(source);
            Compose(source, target);
        }

        private void ApplyPostProcess(RenderTexture source)
        {
#if UNITY_EDITOR
            CreateDefaultSettingsIfNoneLinked();
#endif

            var isBloomEnabled = settings.bloomEnabled;

            Downsample(source);
            Bloom(isBloomEnabled);
            Precompose(isBloomEnabled);
        }

        private void Downsample(RenderTexture source)
        {
            float oneOverOneMinusBloomThreshold = 1f / (1f - settings.bloomThreshold);
            var luma = settings.bloomLumaVector;
            Vector4 luminanceConst = new Vector4(
                luma.x * oneOverOneMinusBloomThreshold,
                luma.y * oneOverOneMinusBloomThreshold,
                luma.z * oneOverOneMinusBloomThreshold,
                -settings.bloomThreshold * oneOverOneMinusBloomThreshold);

            _downsampleMaterial.SetVector(Uniforms._LuminanceConst, luminanceConst);

            Blit(source, _downsampledBrightpassTexture, _downsampleMaterial);
        }

        private void Bloom(bool isBloomEnabled)
        {
            if (isBloomEnabled)
            {
                Blit(_downsampledBrightpassTexture, _brightPassBlurTexture, _horizontalBlurMaterial);
                Blit(_brightPassBlurTexture, _verticalBlurTexture, _verticalBlurMaterial);
            }
        }

        private void Precompose(bool isBloomEnabled)
        {
            var isVignetteEnabledInSettings = settings.vignetteEnabled;
            if (isVignetteEnabledInSettings && !_isVignetteAlreadyEnabled)
            {
                _preComposeMaterial.EnableKeyword(Keywords.VIGNETTE_ON);
                _isVignetteAlreadyEnabled = true;
            }
            else if (!isVignetteEnabledInSettings && _isVignetteAlreadyEnabled)
            {
                _preComposeMaterial.DisableKeyword(Keywords.VIGNETTE_ON);
                _isVignetteAlreadyEnabled = false;
            }

            if (isVignetteEnabledInSettings)
            {
                float vignetteBeginRadius = settings.vignetteBeginRadius;
                float squareVignetteBeginRaduis = vignetteBeginRadius * vignetteBeginRadius;
                float vignetteRadii = vignetteBeginRadius + settings.vignetteExpandRadius;
                float oneOverVignetteRadiusDistance = 1f / (vignetteRadii - squareVignetteBeginRaduis);

                var vignetteColor = settings.vignetteColor;

                _preComposeMaterial.SetVector(Uniforms._VignetteShape, new Vector4(
                    4f * oneOverVignetteRadiusDistance * oneOverVignetteRadiusDistance,
                    -oneOverVignetteRadiusDistance * squareVignetteBeginRaduis));

                _preComposeMaterial.SetColor(Uniforms._VignetteColor, new Color(
                        vignetteColor.r * vignetteColor.a,
                        vignetteColor.g * vignetteColor.a,
                        vignetteColor.b * vignetteColor.a,
                        vignetteColor.a));
            }

            if (isBloomEnabled)
            {
                _preComposeMaterial.SetFloat(Uniforms._BloomIntencity, settings.bloomIntensity);
                _preComposeMaterial.SetColor(Uniforms._BloomTint, settings.bloomTint);

                if (!_isBloomAlreadyEnabled)
                {
                    _preComposeMaterial.EnableKeyword(Keywords.BLOOM_ON);
                    _isBloomAlreadyEnabled = true;
                }
            }
            else if (_isBloomAlreadyEnabled)
            {
                _preComposeMaterial.DisableKeyword(Keywords.BLOOM_ON);
                _isBloomAlreadyEnabled = false;
            }

            Blit(_downsampledBrightpassTexture, _preComposeTexture, _preComposeMaterial);
        }

        private void Compose(RenderTexture source, RenderTexture target)
        {
            Color colorize = settings.colorize;
            var a = colorize.a;
            var colorizeConstant = new Color(colorize.r * a, colorize.g * a, colorize.b * a, 1f - a);
            _composeMaterial.SetColor(Uniforms._Colorize, colorizeConstant);

            if (settings.colorizeEnabled && !_isColorizeAlreadyEnabled)
            {
                _composeMaterial.EnableKeyword(Keywords.COLORIZE_ON);
                _isColorizeAlreadyEnabled = true;
            }
            else if (!settings.colorizeEnabled && _isColorizeAlreadyEnabled)
            {
                _composeMaterial.DisableKeyword(Keywords.COLORIZE_ON);
                _isColorizeAlreadyEnabled = false;
            }

            Blit(source, target, _composeMaterial);
        }

        private void CreateResources()
        {
            _mainCamera = GetComponent<Camera>();

            var downsampleShader = Shader.Find("Sleek Render/Post Process/Downsample Brightpass");
            var horizontalBlurShader = Shader.Find("Sleek Render/Post Process/Horizontal Blur");
            var verticalBlurShader = Shader.Find("Sleek Render/Post Process/Vertical Blur");
            var composeShader = Shader.Find("Sleek Render/Post Process/Compose");
            var preComposeShader = Shader.Find("Sleek Render/Post Process/PreCompose");

            _downsampleMaterial = new Material(downsampleShader);
            _horizontalBlurMaterial = new Material(horizontalBlurShader);
            _verticalBlurMaterial = new Material(verticalBlurShader);
            _preComposeMaterial = new Material(preComposeShader);
            _composeMaterial = new Material(composeShader);

            _currentCameraPixelWidth = Mathf.RoundToInt(_mainCamera.pixelWidth);
            _currentCameraPixelHeight = Mathf.RoundToInt(_mainCamera.pixelHeight);

            int width = _currentCameraPixelWidth;
            int height = _currentCameraPixelHeight;

            var maxHeight = Mathf.Min(height, 720);
            var ratio = (float)maxHeight / height;

            const float squareAspectWidthCorrection = 0.7f;
            int blurHeight = settings.bloomTextureHeight;
            int blurWidth = settings.preserveAspectRatio ? Mathf.RoundToInt(blurHeight * _mainCamera.aspect * squareAspectWidthCorrection) : settings.bloomTextureWidth;

            int downsampleWidth = Mathf.RoundToInt((width * ratio) / 5f);
            int downsampleHeight = Mathf.RoundToInt((height * ratio) / 5f);

            _downsampledBrightpassTexture = CreateTransientRenderTexture("Bloom Downsample Pass", downsampleWidth, downsampleHeight);
            _brightPassBlurTexture = CreateTransientRenderTexture("Pre Bloom", blurWidth, blurHeight);
            _horizontalBlurTexture = CreateTransientRenderTexture("Horizontal Blur", blurWidth, blurHeight);
            _verticalBlurTexture = CreateTransientRenderTexture("Vertical Blur", blurWidth, blurHeight);
            _preComposeTexture = CreateTransientRenderTexture("Pre Compose", downsampleWidth, downsampleHeight);

            _verticalBlurMaterial.SetTexture(Uniforms._MainTex, _downsampledBrightpassTexture);
            _verticalBlurMaterial.SetTexture(Uniforms._BloomTex, _horizontalBlurTexture);

            var xSpread = 1 / (float)blurWidth;
            var ySpread = 1 / (float)blurHeight;
            var blurTexelSize = new Vector4(xSpread, ySpread);
            _verticalBlurMaterial.SetVector(Uniforms._TexelSize, blurTexelSize);
            _horizontalBlurMaterial.SetVector(Uniforms._TexelSize, blurTexelSize);

            _preComposeMaterial.SetTexture(Uniforms._BloomTex, _verticalBlurTexture);

            var downsampleTexelSize = new Vector4(1f / _downsampledBrightpassTexture.width, 1f / _downsampledBrightpassTexture.height);
            _downsampleMaterial.SetVector(Uniforms._TexelSize, downsampleTexelSize);

            _composeMaterial.SetTexture(Uniforms._PreComposeTex, _preComposeTexture);
            _composeMaterial.SetVector(Uniforms._LuminanceConst, new Vector4(0.2126f, 0.7152f, 0.0722f, 0f));

            var renderCameraGameObject = new GameObject("Bloom Render Camera");
            renderCameraGameObject.hideFlags = HideFlags.HideAndDontSave;

            _fullscreenQuadMesh = CreateScreenSpaceQuadMesh();

            _isColorizeAlreadyEnabled = false;
            _isBloomAlreadyEnabled = false;
            _isVignetteAlreadyEnabled = false;
        }

        private RenderTexture CreateTransientRenderTexture(string textureName, int width, int height)
        {
            var renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            renderTexture.name = textureName;
            renderTexture.filterMode = FilterMode.Bilinear;
            renderTexture.wrapMode = TextureWrapMode.Clamp;
            return renderTexture;
        }

        private RenderTexture CreateMainRenderTexture(int width, int height)
        {
            var isMetal = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;
            var isTegra = SystemInfo.graphicsDeviceName.Contains("NVIDIA");
            var rgb565NotSupported = !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB565);

            var textureFormat = RenderTextureFormat.RGB565;
            if (isMetal || isTegra || rgb565NotSupported)
            {
                textureFormat = RenderTextureFormat.ARGB32;
            }

#if UNITY_EDITOR
            textureFormat = RenderTextureFormat.ARGB32;
#endif

            var renderTexture = new RenderTexture(width, height, 16, textureFormat);
            var antialiasingSamples = QualitySettings.antiAliasing;
            renderTexture.antiAliasing = antialiasingSamples == 0 ? 1 : antialiasingSamples;
            return renderTexture;
        }

        private void ReleaseResources()
        {
            DestroyImmediateIfNotNull(_downsampleMaterial);
            DestroyImmediateIfNotNull(_horizontalBlurMaterial);
            DestroyImmediateIfNotNull(_verticalBlurMaterial);
            DestroyImmediateIfNotNull(_preComposeMaterial);
            DestroyImmediateIfNotNull(_composeMaterial);

            DestroyImmediateIfNotNull(_downsampledBrightpassTexture);
            DestroyImmediateIfNotNull(_brightPassBlurTexture);
            DestroyImmediateIfNotNull(_horizontalBlurTexture);
            DestroyImmediateIfNotNull(_verticalBlurTexture);
            DestroyImmediateIfNotNull(_preComposeTexture);

            DestroyImmediateIfNotNull(_fullscreenQuadMesh);
        }

        private void DestroyImmediateIfNotNull(Object obj)
        {
            if (obj != null)
            {
                DestroyImmediate(obj);
            }
        }

        public void Blit(Texture source, RenderTexture destination, Material material, int materialPass = 0)
        {
            SetActiveRenderTextureAndClear(destination);
            this.DrawFullscreenQuad(source, material, materialPass);
        }

        private static void SetActiveRenderTextureAndClear(RenderTexture destination)
        {
            RenderTexture.active = destination;
            GL.Clear(true, true, new Color(1f, 0.75f, 0.5f, 0.8f));
        }

        private void DrawFullscreenQuad(Texture source, Material material, int materialPass = 0)
        {
            material.SetTexture(Uniforms._MainTex, source);
            material.SetPass(materialPass);
            Graphics.DrawMeshNow(_fullscreenQuadMesh, Matrix4x4.identity);
        }

        private void CheckScreenSizeAndRecreateTexturesIfNeeded(Camera mainCamera)
        {
            var cameraSizeHasChanged = mainCamera.pixelWidth != _currentCameraPixelWidth ||
                                       mainCamera.pixelHeight != _currentCameraPixelHeight;

            var bloomSizeHasChanged = _horizontalBlurTexture.width != settings.bloomTextureWidth ||
                                      _horizontalBlurTexture.height != settings.bloomTextureHeight;

            // XORing already changed vs preserve aspect
            // True only when values are different
            bloomSizeHasChanged |= _isAlreadyPreservingAspectRatio ^ settings.preserveAspectRatio;

            if (cameraSizeHasChanged || bloomSizeHasChanged)
            {
                ReleaseResources();
                CreateResources();
            }
        }

        private void CreateDefaultSettingsIfNoneLinked()
        {
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<SleekRenderSettings>();
                settings.name = "Default Settings";
            }
        }

        private Mesh CreateScreenSpaceQuadMesh()
        {
            var mesh = new Mesh();

            var vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), // BL
                new Vector3(-1f, 1f, 0f),  // TL
                new Vector3(1f, 1f, 0f),   // TR
                new Vector3(1f, -1f, 0f)   // BR
            };

            var uvs = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };

            var colors = new[]
            {
                new Color(0f, 0f, 1f),
                new Color(0f, 1f, 1f),
                new Color(1f, 1f, 1f),
                new Color(1f, 0f, 1f),
            };

            var triangles = new[]
            {
                0, 2, 1,
                0, 3, 2
            };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.UploadMeshData(true);

            return mesh;
        }
    }
}