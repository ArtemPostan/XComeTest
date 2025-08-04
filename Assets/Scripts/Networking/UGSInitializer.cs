// Assets/Scripts/Core/UGSInitializer.cs

using Unity.Services.Core;
using Unity.Services.Authentication;
using System.Threading.Tasks;

public static class UGSInitializer
{
    public static async Task InitializeAsync()
    {
        if (Unity.Services.Core.UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
}
