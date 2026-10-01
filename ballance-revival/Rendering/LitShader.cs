using Raylib_cs;

namespace BallanceRevival.Rendering;

/// <summary>Single directional light + ambient, approximating Virtools' fixed-function vertex lighting.</summary>
public static class LitShader
{
    private const string VertexSource = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec3 vertexNormal;
        uniform mat4 mvp;
        uniform mat4 matNormal;
        out vec2 fragTexCoord;
        out vec3 fragNormal;
        void main()
        {
            fragTexCoord = vertexTexCoord;
            fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        in vec3 fragNormal;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        out vec4 finalColor;
        const vec3 lightDir = normalize(vec3(-0.4, 1.0, -0.3));
        void main()
        {
            vec4 texel = texture(texture0, fragTexCoord) * colDiffuse;
            if (texel.a < 0.05) discard;
            float diffuse = max(dot(normalize(fragNormal), lightDir), 0.0);
            finalColor = vec4(texel.rgb * (0.55 + 0.55 * diffuse), texel.a);
        }
        """;

    public static Shader Shader { get; private set; }

    public static void Load()
    {
        Shader = Raylib.LoadShaderFromMemory(VertexSource, FragmentSource);
    }

    public static void Unload()
    {
        if (Shader.Id != 0) Raylib.UnloadShader(Shader);
        Shader = default;
    }
}
