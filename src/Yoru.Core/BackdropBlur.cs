namespace Yoru.Core;

// Small, downsampled navigation backdrops only. Sliding windows keep work O(pixels),
// independent of the blur radius; Android 12+ uses a GPU RenderEffect instead.
public static class BackdropBlur
{
    public static int[] Apply(int[] pixels,int width,int height,int radius=3)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if(width<=0 || height<=0 || (long)width*height!=pixels.Length || radius<0 || radius>32)throw new ArgumentOutOfRangeException(nameof(width));
        var result=(int[])pixels.Clone();if(radius==0)return result;
        var scratch=new int[result.Length];
        for(var pass=0;pass<3;pass++){Filter(result,scratch,width,height,radius,true);Filter(scratch,result,width,height,radius,false);}
        return result;
    }
    private static void Filter(int[] source,int[] target,int width,int height,int radius,bool horizontal)
    {
        var length=horizontal?width:height;var lines=horizontal?height:width;var kernel=radius*2+1;
        for(var line=0;line<lines;line++){
            var a=0;var r=0;var g=0;var b=0;
            int Pixel(int position)=>source[horizontal?line*width+Math.Clamp(position,0,length-1):Math.Clamp(position,0,length-1)*width+line];
            void Add(int value,int sign){a+=((value>>>24)&255)*sign;r+=((value>>>16)&255)*sign;g+=((value>>>8)&255)*sign;b+=(value&255)*sign;}
            for(var k=-radius;k<=radius;k++)Add(Pixel(k),1);
            for(var pos=0;pos<length;pos++){
                target[horizontal?line*width+pos:pos*width+line]=((a/kernel)<<24)|((r/kernel)<<16)|((g/kernel)<<8)|(b/kernel);
                Add(Pixel(pos-radius),-1);Add(Pixel(pos+radius+1),1);
            }
        }
    }
}
