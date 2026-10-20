namespace RuletaObs.App;

// El Client ID de una app pública de Twitch no es un secreto.
// En releases oficiales, CI sobrescribe este archivo usando la variable
// TWITCH_CLIENT_ID del repositorio. Nunca colocar aquí un Client Secret.
internal static class BuiltInTwitchClientId
{
    public const string Value = "";
}
