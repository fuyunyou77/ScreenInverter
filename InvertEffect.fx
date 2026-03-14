sampler2D input : register(s0);    // 主屏幕截图纹理
sampler2D blendMap : register(s1); // 空间平滑亮度图 (由 CPU 生成并模糊)

// Parameters bound from WPF
float Mode          : register(c0);  // 0=智能暗色, 1=柔和全反, 2=强力全反
float DarkThreshold : register(c1);  // 混合曲线：开始反色的区域亮度
float BrightThreshold : register(c2); // 混合曲线：完全反色的区域亮度
float TargetDarkLevel : register(c3); // 目标底色亮度 (VSCode 风格)

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 pixelColor = tex2D(input, uv);
    
    // 采样平滑亮度图
    // 虽然 blendMap 也是一张图片，但它反映的是像素所在区域的平均亮度。
    // 因为它是经过模糊处理的，相邻像素采样到的 regionLuma 几乎一样。
    float regionLuma = tex2D(blendMap, uv).r;
    
    // --- 模式选择 (branchless) ---
    float mode2 = step(1.5, Mode);
    float mode1 = step(0.5, Mode) * (1.0 - mode2);
    float mode0 = 1.0 - step(0.5, Mode);
    
    // 1. 强力反色 (Mode 2)
    float3 fullInv = 1.0 - pixelColor.rgb;
    
    // 2. 柔和反色 (Mode 1)
    float fl = 25.0 / 255.0;
    float rng = 105.0 / 255.0;
    float3 softInv = fl + (1.0 - pixelColor.rgb) * rng;
    
    // 3. 智能空间反色 (Mode 0)
    // 根据区域平均亮度计算混合因子 (Smoothstep)
    float t = saturate((regionLuma - DarkThreshold) / (BrightThreshold - DarkThreshold + 0.001));
    float blendFactor = t * t * (3.0 - 2.0 * t);
    
    // 计算该像素的 VSCode 风格反色值
    float luma = dot(pixelColor.rgb, float3(0.299, 0.587, 0.114));
    float invertedLuma = TargetDarkLevel + (1.0 - luma) * (1.0 - 2.0 * TargetDarkLevel);
    
    // 对反色部分执行轻微降饱和处理，增加舒适度
    float satScale = lerp(1.0, 0.85, blendFactor);
    float3 centered = (pixelColor.rgb - luma) * satScale;
    float3 smartInv = clamp(centered + invertedLuma, 0.0, 1.0);
    
    // 在原色和反色之间根据 [区域亮度] 进行空间平滑混合
    // 这个平滑混合是【去锯齿】的关键：文字边缘像素由于采样到相同的 blendFactor，其原始抗锯齿比例得以保留
    float3 smartResult = lerp(pixelColor.rgb, smartInv, blendFactor);
    
    color.rgb = smartResult * mode0 + softInv * mode1 + fullInv * mode2;
    color.a = pixelColor.a;
    
    return color;
}
