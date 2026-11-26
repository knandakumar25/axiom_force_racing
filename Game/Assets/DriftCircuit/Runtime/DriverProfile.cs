namespace DriftCircuit
{
    internal enum ChassisStyle
    {
        KatanaFin,
        SolarRam,
        HydroFoil,
        PhantomDart,
        TitanCoil,
        PrismWing
    }

    /// <summary>Defines a playable Axiom Force operative's handling stats and placeholder kart styling.</summary>
    internal sealed class DriverProfile
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Codename { get; }
        public string Role { get; }
        public uint PrimaryColor { get; }
        public uint AccentColor { get; }
        public float TopSpeed { get; }
        public float BoostSpeed { get; }
        public float Acceleration { get; }
        public float SteeringScale { get; }
        public float DriftChargeRate { get; }
        public float BoostDuration { get; }
        public ChassisStyle Chassis { get; }

        public DriverProfile(string id, string displayName, string codename, string role,
            uint primaryColor, uint accentColor, float topSpeed, float boostSpeed,
            float acceleration, float steeringScale, float driftChargeRate, float boostDuration,
            ChassisStyle chassis)
        {
            Id = id;
            DisplayName = displayName;
            Codename = codename;
            Role = role;
            PrimaryColor = primaryColor;
            AccentColor = accentColor;
            TopSpeed = topSpeed;
            BoostSpeed = boostSpeed;
            Acceleration = acceleration;
            SteeringScale = steeringScale;
            DriftChargeRate = driftChargeRate;
            BoostDuration = boostDuration;
            Chassis = chassis;
        }

        public static readonly DriverProfile Default = new(
            "default", "Baseline Kart", "Prototype", "Standard",
            0x18a4b9, 0xffd47b, 25f, 34f, 13f, 1f, 1f, 1f, ChassisStyle.KatanaFin);

        public static readonly DriverProfile CyberShogun = new(
            "cyber-shogun", "Cyber Shogun", "Kaito Kurogane", "Balanced Vanguard Striker",
            0x18a4b9, 0xe63946, 25.5f, 34.5f, 13.5f, 1.02f, 1.05f, 1.00f, ChassisStyle.KatanaFin);

        public static readonly DriverProfile SuryaPrime = new(
            "surya-prime", "Surya Prime", "Arjun Varma", "Solar Bulwark Tank",
            0xd97706, 0xfde047, 27.0f, 36.0f, 11.5f, 0.90f, 0.92f, 1.10f, ChassisStyle.SolarRam);

        public static readonly DriverProfile Oceanus = new(
            "oceanus", "Oceanus", "Kiana Vaea", "Hydro Vanguard Tactician",
            0x0284c7, 0x67e8f9, 24.5f, 33.5f, 13.8f, 1.12f, 1.08f, 1.00f, ChassisStyle.HydroFoil);

        public static readonly DriverProfile MirageBlade = new(
            "mirage-blade", "Mirage Blade", "Zayd Al-Mansoor", "Phantom Infiltrator Assassin",
            0x6d28d9, 0xc4b5fd, 24.8f, 34.2f, 15.0f, 1.10f, 1.22f, 0.95f, ChassisStyle.PhantomDart);

        public static readonly DriverProfile ApexTitan = new(
            "apex-titan", "Apex Titan", "King Tariq Nzinga", "Electro-Magnetic Juggernaut",
            0x334155, 0x38bdf8, 27.5f, 36.5f, 11.0f, 0.86f, 0.95f, 1.15f, ChassisStyle.TitanCoil);

        public static readonly DriverProfile AuraPhoenicia = new(
            "aura-phoenicia", "Aura Phoenicia", "Maya Khoury", "Ultrasonic Prism Weaver",
            0xdb2777, 0xf9a8d4, 25.0f, 34.4f, 14.2f, 1.06f, 1.25f, 1.05f, ChassisStyle.PrismWing);

        public static readonly DriverProfile[] All =
        {
            CyberShogun,
            SuryaPrime,
            Oceanus,
            MirageBlade,
            ApexTitan,
            AuraPhoenicia
        };

        /// <summary>Returns a concise stat summary for UI display.</summary>
        public string StatSummary =>
            $"SPD {TopSpeed:0.0}  ACC {Acceleration:0.0}  HDL {SteeringScale:0.00}x  DRF {DriftChargeRate:0.00}x";

        /// <summary>Returns the next driver in the roster, wrapping around.</summary>
        public static int NextIndex(int index) => (index + 1) % All.Length;

        /// <summary>Picks a distinct rival driver from the roster.</summary>
        public static DriverProfile RivalFor(DriverProfile playerDriver)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id != playerDriver?.Id)
                    return All[i];
            }
            return SuryaPrime;
        }
    }
}
