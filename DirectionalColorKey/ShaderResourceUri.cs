namespace DirectionalColorKey;

internal class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new Uri($"pack://application:,,,/DirectionalColorKey;component/Resources/Shader/{shaderName}.cso", UriKind.Absolute);
}
