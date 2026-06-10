using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DirectionalColorKey
{
    internal class ShaderResourceUri
    {
        public static Uri Get(string shaderName) => new Uri($"pack://application:,,,/DirectionalColorKey;component/Shaders/{shaderName}.cso", UriKind.Absolute);
    }
}
