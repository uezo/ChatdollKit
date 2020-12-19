using System;

namespace ChatdollKit.Extension.Azure
{
    public class AzureLanguage
    {
        public string Name { get; set; }
        public string SpeechCode { get; set; }
        public string SpeakerName { get; set; }
        public string SpeakerGender { get; set; }
        public string TranslationCode { get; set; }
        public bool IsSupported { get; set; }

        public AzureLanguage(string name)
        {
            // Set Name and IsSupported
            Name = name;
            IsSupported = true;

            // Set params when matched
            if (name.Contains("アラビア語")) { SpeechCode = "ar-EG"; SpeakerGender = "Female"; SpeakerName = "ar-EG-Hoda"; TranslationCode = "ar"; }
            else if (name.Contains("カタルニア語") || name.Contains("カタルーニャ語")) { SpeechCode = "ca-ES"; SpeakerGender = "Female"; SpeakerName = "ca-ES-HerenaRUS"; TranslationCode = "ca"; }
            else if (name.Contains("デンマーク語")) { SpeechCode = "da-DK"; SpeakerGender = "Female"; SpeakerName = "da-DK-HelleRUS"; TranslationCode = "da"; }
            else if (name.Contains("ドイツ語")) { SpeechCode = "de-DE"; SpeakerGender = "Female"; SpeakerName = "de-DE-HeddaRUS"; TranslationCode = "de"; }
            else if (name.Contains("英語")) { SpeechCode = "en-US"; SpeakerGender = "Female"; SpeakerName = "en-US-JessaRUS"; TranslationCode = "en"; }
            else if (name.Contains("スペイン語")) { SpeechCode = "es-ES"; SpeakerGender = "Female"; SpeakerName = "es-ES-HelenaRUS"; TranslationCode = "es"; }
            else if (name.Contains("フィンランド語")) { SpeechCode = "fi-FI"; SpeakerGender = "Female"; SpeakerName = "fi-FI-HeidiRUS"; TranslationCode = "fi"; }
            else if (name.Contains("フランス語")) { SpeechCode = "fr-FR"; SpeakerGender = "Female"; SpeakerName = "fr-FR-HortenseRUS"; TranslationCode = "fr"; }
            else if (name.Contains("ヒンディー語")) { SpeechCode = "hi-IN"; SpeakerGender = "Female"; SpeakerName = "hi-IN-Kalpana"; TranslationCode = "hi"; }
            else if (name.Contains("イタリア語")) { SpeechCode = "it-IT"; SpeakerGender = "Female"; SpeakerName = "it-IT-LuciaRUS"; TranslationCode = "it"; }
            else if (name.Contains("日本語")) { SpeechCode = "ja-JP"; SpeakerGender = "Female"; SpeakerName = "ja-JP-HarukaRUS"; TranslationCode = "ja"; }
            else if (name.Contains("韓国語")) { SpeechCode = "ko-KR"; SpeakerGender = "Female"; SpeakerName = "ko-KR-HeamiRUS"; TranslationCode = "ko"; }
            else if (name.Contains("ノルウェー語")) { SpeechCode = "nb-NO"; SpeakerGender = "Female"; SpeakerName = "nb-NO-HuldaRUS"; TranslationCode = "nb"; }
            else if (name.Contains("オランダ語")) { SpeechCode = "nl-NL"; SpeakerGender = "Female"; SpeakerName = "nl-NL-HannaRUS"; TranslationCode = "nl"; }
            else if (name.Contains("ポーランド語")) { SpeechCode = "pl-PL"; SpeakerGender = "Female"; SpeakerName = "pl-PL-PaulinaRUS"; TranslationCode = "pl"; }
            else if (name.Contains("ポルトガル語")) { SpeechCode = "pt-PT"; SpeakerGender = "Female"; SpeakerName = "pt-PT-HeliaRUS"; TranslationCode = "pt-pt"; }
            else if (name.Contains("ロシア語")) { SpeechCode = "ru-RU"; SpeakerGender = "Female"; SpeakerName = "ru-RU-EkaterinaRUS"; TranslationCode = "ru"; }
            else if (name.Contains("スウェーデン語")) { SpeechCode = "sv-SE"; SpeakerGender = "Female"; SpeakerName = "sv-SE-HedvigRUS"; TranslationCode = "sv"; }
            else if (name.Contains("トルコ語")) { SpeechCode = "tr-TR"; SpeakerGender = "Female"; SpeakerName = "tr-TR-SedaRUS"; TranslationCode = "tr"; }
            else if (name.Contains("中国語")) { SpeechCode = "zh-CN"; SpeakerGender = "Female"; SpeakerName = "zh-CN-HuihuiRUS"; TranslationCode = "zh-Hans"; }
            else if (name.Contains("広東語")) { SpeechCode = "zh-HK"; SpeakerGender = "Female"; SpeakerName = "zh-HK-Tracy-Apollo"; TranslationCode = "yue"; }
            else if (name.Contains("台湾語")) { SpeechCode = "zh-TW"; SpeakerGender = "Female"; SpeakerName = "zh-TW-HanHanRUS"; TranslationCode = "zh-Hant"; }
            else
            {
                IsSupported = false;
            }
        }

        public AzureLanguage(string name, string speechCode, string speakerName, string speakerGender, string translationCode)
        {
            Name = name;
            SpeechCode = speechCode;
            SpeakerName = speakerName;
            SpeakerGender = speakerGender;
            TranslationCode = translationCode;
            IsSupported = true;
        }
    }
}
