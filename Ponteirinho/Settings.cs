using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ponteirinho
{
    /// <summary>De qual lado da tela a vareta sai.</summary>
    internal enum StickSide
    {
        Right,
        Center,
        Left,
    }

    /// <summary>Preferências salvas em %APPDATA%\Ponteirinho\settings.json.</summary>
    internal sealed class Settings
    {
        public static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ponteirinho");

        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public StickSide Side { get; set; } = StickSide.Right;

        /// <summary>Largura da mão em pixels "lógicos" (já acompanha a escala do monitor).</summary>
        public double HandWidth { get; set; } = 96;

        public bool StartEnabled { get; set; } = true;

        /// <summary>Tipo de mão (ver HandStyle.All).</summary>
        public string Style { get; set; } = "th-lavender";

        /// <summary>false = vareta preta (padrão), true = madeira.</summary>
        public bool WoodStick { get; set; } = false;

        /// <summary>Esconde o cursor do Windows enquanto a mão aparece.</summary>
        public bool HideCursor { get; set; } = true;

        public string Color { get; set; } = "#B7A4F2";

        public ClickSoundKind Sound { get; set; } = ClickSoundKind.Pop;

        /// <summary>0 = baixo, 1 = médio, 2 = alto.</summary>
        public int Volume { get; set; } = 1;

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
            }
            catch
            {
                // Arquivo corrompido: volta ao padrão.
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
            catch
            {
                // Não conseguir salvar preferência não deve derrubar o app.
            }
        }
    }
}
