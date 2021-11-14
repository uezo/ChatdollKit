using UnityEngine;

// Uncomment after adding ZXing in your project
// https://github.com/micjahn/ZXing.Net/releases

//using ZXing;
//using ZXing.Common;
//using ZXing.QrCode;

namespace ChatdollKit.Examples.MultiSkills
{
    public class QRCodeDecoder
    {
        public static string DecodeByZXing(Texture2D texture)
        {
            // Uncomment after adding ZXing in your project

            //var luminanceSource = new Color32LuminanceSource(texture.GetPixels32(), texture.width, texture.height);
            //var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(luminanceSource)));
            //if (result != null)
            //{
            //    return result.Text;
            //}
            //else
            //{
            //    return string.Empty;
            //}

            return string.Empty;
        }
    }
}
