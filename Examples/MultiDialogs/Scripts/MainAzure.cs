using UnityEngine;
using ChatdollKit.Examples.Dialogs;
using ChatdollKit.Extension.Azure;

namespace ChatdollKit.Examples.MultiDialog
{
    [RequireComponent(typeof(WeatherDialog))]
    [RequireComponent(typeof(TranslateDialog))]
    [RequireComponent(typeof(EchoDialog))]
    [RequireComponent(typeof(Router))]
    public class MainAzure : AzureApplication
    {
        
    }
}
