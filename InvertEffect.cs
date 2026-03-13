using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ScreenInverter;

/// <summary>
/// GPU-accelerated color inversion. 
/// Formula: Output = Floor + (1.0 - Input) * Range
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
        UpdateShaderValue(OutputRangeProperty);
        UpdateShaderValue(OutputFloorProperty);
    }

    private static void InitializeShader()
    {
        if (_pixelShaderPool != null) return;

        try
        {
            string hlsl = @"
sampler2D implicitInput : register(s0);
float range : register(c0);
float floor : register(c1);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(implicitInput, uv);
    c.rgb = floor + (1.0 - c.rgb) * range;
    return c;
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
            // If primary compilation fails, we allow it to be null here. 
            // In the next step we could add a hardcoded Base64 if needed, 
            // but let's see if the fix to ShaderCompiler works first.
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

    public double OutputRange
    {
        get => (double)GetValue(OutputRangeProperty);
        set => SetValue(OutputRangeProperty, value);
    }

    public static readonly DependencyProperty OutputRangeProperty =
        DependencyProperty.Register("OutputRange", typeof(double), typeof(InvertEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(0)));

    public double OutputFloor
    {
        get => (double)GetValue(OutputFloorProperty);
        set => SetValue(OutputFloorProperty, value);
    }

    public static readonly DependencyProperty OutputFloorProperty =
        DependencyProperty.Register("OutputFloor", typeof(double), typeof(InvertEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(1)));
}
