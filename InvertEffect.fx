sampler2D input : register(s0);

// Parameters bound from WPF
float OutputRange : register(c0);
float OutputFloor : register(c1);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // Sample original pixel
    float4 color = tex2D(input, uv);
    
    // InvertRGB (only RGB, keeping Alpha unchanged)
    // Formula: Floor + (1.0 - color) * Range
    color.rgb = OutputFloor + (1.0 - color.rgb) * OutputRange;
    
    return color;
}
