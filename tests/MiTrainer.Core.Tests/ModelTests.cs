using System.Text.Json;
using MiTrainer.Core.Cheats;
using MiTrainer.Core.Values;

namespace MiTrainer.Core.Tests;

public class ModelTests
{
    [Theory]
    [InlineData("1A2B", 0x1A2B)]
    [InlineData("0x1A2B", 0x1A2B)]
    [InlineData(" ff ", 0xFF)]
    [InlineData("-10", -0x10)]
    public void Lee_hexadecimal_como_lo_escribe_el_usuario(string text, long expected)
    {
        Assert.True(NumberText.TryParseHex(text, out long value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("5.5", 5.5)]
    [InlineData("5,5", 5.5)]
    public void Acepta_coma_y_punto_decimal(string text, double expected)
    {
        Assert.True(NumberText.TryParseDecimal(text, out double value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Los_perfiles_antiguos_siguen_leyendose()
    {
        const string json = """
            { "juego": "Mi Juego", "proceso": "MiJuego.exe", "cheats": [
              { "nombre": "Velocidad", "atajo": "F1", "tipo": "congelar", "modulo": "MiJuego.exe",
                "direccion": "1A2B3C", "offsets": ["10", "8"], "tipoValor": "float", "valor": 2.5 },
              { "nombre": "Munición", "tipo": "parche", "patron": "89 4B 10", "desplazamiento": 4, "bytes": "90 90 90" } ] }
            """;

        var profile = JsonSerializer.Deserialize<GameProfile>(json)!;

        Assert.Equal(CheatType.Freeze, profile.Cheats[0].Type);
        Assert.Equal(ValueKind.Float, profile.Cheats[0].ValueType);
        Assert.Equal(["10", "8"], profile.Cheats[0].Offsets);
        Assert.Equal(CheatType.Patch, profile.Cheats[1].Type);
        Assert.Equal(4, profile.Cheats[1].PatternOffset);
    }

    [Fact]
    public void Los_tipos_se_guardan_con_sus_nombres_en_espanol()
    {
        var definition = new CheatDefinition { Name = "x", Type = CheatType.Patch, ValueType = ValueKind.Double };

        string json = JsonSerializer.Serialize(definition);

        Assert.Contains("\"tipo\":\"parche\"", json);
        Assert.Contains("\"tipoValor\":\"double\"", json);
    }

    [Fact]
    public void Duplicar_con_with_no_toca_el_original()
    {
        var original = new CheatDefinition { Name = "Vida", Hotkey = "F1" };

        var copy = original with { Name = "Vida (copia)", Hotkey = "F2" };

        Assert.Equal("Vida", original.Name);
        Assert.Equal("F2", copy.Hotkey);
    }
}
