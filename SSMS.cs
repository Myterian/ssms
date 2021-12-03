// Modified version of Keijiro Streak effect
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace byteslider.PostProcessing.SSMS
{
    [PostProcess(typeof(SSMSRenderer), PostProcessEvent.BeforeStack, "byteslider/Screen Space Multi Scattering")]
	public class SSMS : PostProcessEffectSettings 
    {
        [ Header("Multi Scatter Fog") ]

        [ Tooltip( "How far off in the distance the effect should start in world units" ) ]
        public FloatParameter startDistance = new FloatParameter { value = 15f };

        [ Tooltip( "Controls how much the fog is overlayed on the scene. Helps to blend the fog in or out, which works best with a density of 0" ), Range(0, 1) ]
        public FloatParameter blend = new FloatParameter { value = 1f };

        [ Tooltip( "How dense the fog is. Affects how harsh the fog starts at the starting distance" ), Range( 0, 0.999f ) ]
        public FloatParameter density = new FloatParameter { value = 0.035f };

        [ Tooltip( "Affects how much the light gets scattered" ), Range(0, 5) ]
        public FloatParameter scattering = new FloatParameter { value = 5f };

        [ Tooltip( "Controls how much light is preserved. Less values mean more energy loss of the light" ), Range(0, 1) ]
        public FloatParameter intensity = new FloatParameter { value = 0.05f };

        [ Tooltip( "Fog tint. Brightness also contributes to intensity" ), ColorUsage(false) ]
        public ColorParameter tint = new ColorParameter { value = new Color( 0.262f, 0.298f, 0.33f ) };

        [ Header("Performance") ]

        [ Tooltip( "Should the effect use a high quality blur or not" ) ]
        public BoolParameter highQuality = new BoolParameter { value = true };

        [ Tooltip( "Uses half as many samples as the effect normally would" ) ]
        public BoolParameter fastMode = new BoolParameter { value = false };

        [Tooltip( "Custom setting for how many samples should be used" ), UnityEngine.Rendering.PostProcessing.Min(0)] 
        public IntParameter customQuality = new IntParameter { value = 0 };

        #if UNITY_EDITOR
        [ Header("Debug") ]

        [ Tooltip( "What pass should be reviewed. 1 is prefilter, 2 is downsample, 3 is upsample" ) ]
        public IntParameter debugPass = new IntParameter { value = 0 };
        #endif
    }


    public sealed class SSMSRenderer : PostProcessEffectRenderer<SSMS>
	{
        // Fetch shader variable ids
        static class ShaderPropertyID
        {
            internal static readonly int StartDistance = Shader.PropertyToID("_StartDistance");
            internal static readonly int Density = Shader.PropertyToID("_Density");
            internal static readonly int Scatter = Shader.PropertyToID("_Scatter");
            internal static readonly int Blend = Shader.PropertyToID("_Blend");
            internal static readonly int Intensity = Shader.PropertyToID("_Intensity");
            internal static readonly int Color = Shader.PropertyToID("_Color");
            internal static readonly int HighTex = Shader.PropertyToID("_HighTex");
        }

        // Future proofing. Avoids large screen resolutions (64k) to go overboard with samples
        const int MaxMipLevel = 16;

        int[] _rtMipDown;
        int[] _rtMipUp;
        
        public override void Init()
        {
            _rtMipDown = new int[MaxMipLevel];
            _rtMipUp = new int[MaxMipLevel];

            for (var i = 0; i < MaxMipLevel; i++) {
                _rtMipDown[i] = Shader.PropertyToID("_MipDown" + i);
                _rtMipUp[i] = Shader.PropertyToID("_MipUp" + i);
            }
        }

        // Fog Renderer
        public override void Render(PostProcessRenderContext context)
        {
            var cmd = context.command;
            cmd.BeginSample("SSMS");

            // Shader uniforms
            var sheet = context.propertySheets.Get(Shader.Find("Hidden/PostProcessing/SSMS"));
            sheet.properties.SetFloat(ShaderPropertyID.StartDistance, settings.startDistance);
            sheet.properties.SetFloat(ShaderPropertyID.Density, 1 - settings.density);
            sheet.properties.SetFloat(ShaderPropertyID.Scatter, settings.scattering);
            sheet.properties.SetColor(ShaderPropertyID.Color, settings.tint);

            // Easier to control transition for blend. Otherwise you need to fiddle with .997 - .999 values
            float _blend = Mathf.Log( Mathf.Pow( 2.71828f, settings.blend ) );
            sheet.properties.SetFloat(ShaderPropertyID.Blend, _blend);

            if( settings.highQuality )
                sheet.EnableKeyword("HIGH_QUALITY_UPSAMPLE");
            else
                sheet.DisableKeyword("HIGH_QUALITY_UPSAMPLE");

            // Figure out how many iterations we need
            // Figure out which side of the screen is larger. For all our portrait mode lovers
            int halfWidth = Mathf.FloorToInt( context.screenWidth / 2f );
            int halfHeight = Mathf.FloorToInt( context.screenHeight / 2f );
            int maxScreenResolution = Mathf.Max( halfWidth, halfHeight );

            //Figure out how many mip iterations we would ideally need to reach our screens resolution
            float log = Mathf.Log( maxScreenResolution, 2f );
            int exponent = Mathf.FloorToInt( log );
            exponent = Mathf.Clamp( exponent, 1, MaxMipLevel );

            // Set up how many mip iteration we will actually use. By default, all of them
            int iteration = exponent;

            if( settings.customQuality != 0 )
                iteration = settings.customQuality;
            else if( settings.fastMode )
                iteration = exponent / 2;

            iteration = Mathf.Clamp( iteration, 1, MaxMipLevel );

            // Adjusts the intensity for the sample (<- iterations) count. Lower samples usually return a darker image than higher samples on the same intensity
            float _intensity = ( settings.intensity * ((float)exponent / (float)iteration) );
            sheet.properties.SetFloat( ShaderPropertyID.Intensity, _intensity );

            // float _density = settings.density * ((float)iteration / (float)exponent);
            // sheet.properties.SetFloat( ShaderPropertyID.Density, _density );

            // Accounts for custom quality setting. With lower iterations than desired, the mips can't be sampled step by step like they normally would. That would result in less blur, 
            // because the sample resolution wouldn't go low enough. We want about the same amount of blur like normal, but with less samples. That's why we spread the samples out, to get 
            // as much mip coverage as we can, by skipping a step in the mip pyramid when we can.
            // The first sample will always be half screen resolution and the last sample will always be the desired smallest screen resolution
            float mipSkip = (float)exponent - 2f / (float)iteration - 2f;
            int levelOffset = 1;

            // Build the MIP pyramid.
            var level = 1;
            for (; level < iteration; level++) 
            {
                Vector2Int screenSize = new Vector2Int( context.screenWidth / levelOffset, context.screenHeight / levelOffset );
                context.GetScreenSpaceTemporaryRT( cmd, _rtMipDown[level], 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, screenSize.x, screenSize.y );

                // First sample will be the pre filter
                if( level == 1 )
                {
                    cmd.BlitFullscreenTriangle(context.source, _rtMipDown[level], sheet, 0);

                    // Debug
                    #if UNITY_EDITOR
                    if( settings.debugPass == 1 )
                    {
                        cmd.BlitFullscreenTriangle(_rtMipDown[level], context.destination);
                        cmd.ReleaseTemporaryRT(_rtMipDown[level]);
                        return;
                    }
                    #endif
                }
                else
                {
                    cmd.BlitFullscreenTriangle(_rtMipDown[level - 1], _rtMipDown[level], sheet, 1);
                    
                    // Debug
                    #if UNITY_EDITOR
                    if( settings.debugPass == 2 )
                    {
                        cmd.ReleaseTemporaryRT(_rtMipDown[level - 1]);
                    }
                    #endif
                }

                // Calculating next screen resolution
                levelOffset = Mathf.Clamp( level + Mathf.CeilToInt( mipSkip ), 1, exponent );
            }

            // Debug
            #if UNITY_EDITOR
            if( settings.debugPass == 2)
            {
                cmd.BlitFullscreenTriangle(_rtMipDown[level], context.destination);
                cmd.ReleaseTemporaryRT(_rtMipDown[level]);
                return;
            }
            #endif

            // Upsample and combine.
            var lastRT = _rtMipDown[--level];
            for (; level >= 1; level--) 
            {
                Vector2Int screenSize = new Vector2Int( context.screenWidth / levelOffset, context.screenHeight / levelOffset );
                context.GetScreenSpaceTemporaryRT( cmd, _rtMipUp[level], 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, screenSize.x, screenSize.y);
                cmd.SetGlobalTexture(ShaderPropertyID.HighTex, _rtMipDown[level]);
                cmd.BlitFullscreenTriangle(lastRT, _rtMipUp[level], sheet, 2);

                cmd.ReleaseTemporaryRT(_rtMipDown[level]);
                cmd.ReleaseTemporaryRT(lastRT);

                lastRT = _rtMipUp[level];

                // Calculating next screen resolution
                levelOffset = ( Mathf.Max(level - levelOffset, 1));
            }

            // Debug
            #if UNITY_EDITOR
            if( settings.debugPass == 3)
            {
                cmd.BlitFullscreenTriangle(lastRT, context.destination);
                cmd.ReleaseTemporaryRT(lastRT);
                return;
            }
            #endif

            // Final composition.
            cmd.SetGlobalTexture(ShaderPropertyID.HighTex, context.source);
            cmd.BlitFullscreenTriangle(lastRT, context.destination, sheet, 3);

            // Cleaning up.
            cmd.ReleaseTemporaryRT(lastRT);
            cmd.EndSample("SSMS");
        }
	}
}
