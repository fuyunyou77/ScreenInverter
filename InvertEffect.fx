sampler2D input : register(s0);

// Parameters bound from WPF
float Mode          : register(c0);  // 0=智能暗色, 1=柔和全反, 2=强力全反
float DarkThreshold : register(c1);  // 暗色阈值 (默认 0.35)
float BrightThreshold : register(c2); // 亮色阈值 (默认 0.70)
float TargetDarkLevel : register(c3); // 目标暗色亮度 (默认 0.12)

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(input, uv);
    
    // --- 模式选择 (branchless) ---
    float mode2 = step(1.5, Mode);
    float mode1 = step(0.5, Mode) * (1.0 - mode2);
    float mode0 = 1.0 - step(0.5, Mode);
    
    // 模式 2：强力全反
    float3 fullInv = 1.0 - color.rgb;
    
    // 模式 1：柔和全反
    float fl = 25.0 / 255.0;
    float rng = 105.0 / 255.0;
    float3 softInv = fl + (1.0 - color.rgb) * rng;
    
    // 模式 0：智能暗色 (逐像素级判断)
    float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
    float invertedLuma = TargetDarkLevel + (1.0 - luma) * (1.0 - 2.0 * TargetDarkLevel);
    
    // 基于像素当前亮度的混合因子 (smoothstep)
    float t = saturate((luma - DarkThreshold) / (BrightThreshold - DarkThreshold + 0.001));
    float blend = t * t * (3.0 - 2.0 * t);
    
    float satScale = lerp(1.0, 0.85, blend);
    float3 centered = (color.rgb - luma) * satScale;
    float newLuma = lerp(luma, invertedLuma, blend);
    float3 smartResult = clamp(centered + newLuma, 0.0, 1.0);
    
    // 最终输出
    color.rgb = smartResult * mode0 + softInv * mode1 + fullInv * mode2;
    
    return color;
}
