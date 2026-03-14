using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ScreenInverter;

/// <summary>
/// GPU-accelerated adaptive color inversion.
/// Supports three modes:
///   0 = Smart Dark (per-pixel inversion + threshold)
///   1 = Soft Full Invert (compressed range)
///   2 = Full Invert (pure math inversion)
/// </summary>
public class InvertEffect : ShaderEffect
{
    private static PixelShader? _pixelShaderPool;
    // We keep a reference to the stream to prevent it from being GC'd while in use by unmanaged code.
    private static MemoryStream? _streamReference;

    public InvertEffect()
    {
        InitializeShader();

        if (_pixelShaderPool != null)
        {
            this.PixelShader = _pixelShaderPool;
        }

        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ModeProperty);
        UpdateShaderValue(DarkThresholdProperty);
        UpdateShaderValue(BrightThresholdProperty);
        UpdateShaderValue(TargetDarkLevelProperty);
    }

    private static void InitializeShader()
    {
        if (_pixelShaderPool != null) return;

        try
        {
            string hlsl = @"
sampler2D implicitInput : register(s0);
float Mode          : register(c0);
float DarkThreshold : register(c1);
float BrightThreshold : register(c2);
float TargetDarkLevel : register(c3);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(implicitInput, uv);
    float mode2 = step(1.5, Mode);
    float mode1 = step(0.5, Mode) * (1.0 - mode2);
    float mode0 = 1.0 - step(0.5, Mode);
    float3 fullInv = 1.0 - color.rgb;
    float fl = 25.0 / 255.0;
    float rng = 105.0 / 255.0;
    float3 softInv = fl + (1.0 - color.rgb) * rng;
    float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
    float invertedLuma = TargetDarkLevel + (1.0 - luma) * (1.0 - 2.0 * TargetDarkLevel);
    float t = saturate((luma - DarkThreshold) / (BrightThreshold - DarkThreshold + 0.001));
    float blend = t * t * (3.0 - 2.0 * t);
    float satScale = lerp(1.0, 0.85, blend);
    float3 centered = (color.rgb - luma) * satScale;
    float newLuma = lerp(luma, invertedLuma, blend);
    float3 smartResult = clamp(centered + newLuma, 0.0, 1.0);
    color.rgb = smartResult * mode0 + softInv * mode1 + fullInv * mode2;
    return color;
}
";
            byte[] bytecode = ShaderCompiler.CompileHlsl(hlsl);
            
            _streamReference = new MemoryStream(bytecode);
            _pixelShaderPool = new PixelShader();
            _pixelShaderPool.SetStreamSource(_streamReference);

            PixelShader.InvalidPixelShaderEncountered += (s, e) => {
                LogSafe("[ShaderRuntimeError] WPF rejected organized PixelShader.");
            };
        }
        catch (Exception ex)
        {
            LogSafe($"[ShaderInitError] {ex.Message}");
        }
    }

    private static void LogSafe(string msg)
    {
        try { File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] {msg}\n"); } catch {}
    }

    // --- Properties ---

    public System.Windows.Media.Brush Input
    {
        get => (System.Windows.Media.Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(InvertEffect), 0);

    /// <summary>
    /// 模式: 0=智能暗色, 1=柔和全反, 2=强力全反
    /// </summary>
    public double Mode
    {
        get => (double)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register("Mode", typeof(double), typeof(InvertEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(0)));

    /// <summary>
    /// 暗色阈值：亮度低于此值的像素不处理 (默认 0.35)
    /// </summary>
    public double DarkThreshold
    {
        get => (double)GetValue(DarkThresholdProperty);
        set => SetValue(DarkThresholdProperty, value);
    }

    public static readonly DependencyProperty DarkThresholdProperty =
        DependencyProperty.Register("DarkThreshold", typeof(double), typeof(InvertEffect),
            new UIPropertyMetadata(0.35, PixelShaderConstantCallback(1)));

    /// <summary>
    /// 亮色阈值：亮度高于此值的像素全力反色 (默认 0.70)
    /// </summary>
    public double BrightThreshold
    {
        get => (double)GetValue(BrightThresholdProperty);
        set => SetValue(BrightThresholdProperty, value);
    }

    public static readonly DependencyProperty BrightThresholdProperty =
        DependencyProperty.Register("BrightThreshold", typeof(double), typeof(InvertEffect),
            new UIPropertyMetadata(0.70, PixelShaderConstantCallback(2)));

    /// <summary>
    /// 目标暗色亮度：反色后的底色亮度 (默认 0.12, 接近 VSCode #1E1E1E)
    /// </summary>
    public double TargetDarkLevel
    {
        get => (double)GetValue(TargetDarkLevelProperty);
        set => SetValue(TargetDarkLevelProperty, value);
    }

    public static readonly DependencyProperty TargetDarkLevelProperty =
        DependencyProperty.Register("TargetDarkLevel", typeof(double), typeof(InvertEffect),
            new UIPropertyMetadata(0.12, PixelShaderConstantCallback(3)));
}
