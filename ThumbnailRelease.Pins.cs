// Release pin from contracts/camera-release-2026-09-23.json; factory icon compatibility pins remain unchanged.
namespace MoreCars.Companion;

internal static class ThumbnailRelease
{
    internal const string ReleaseId = "legacy-cars-2026-09-23.4";
    internal const long ReleaseByteSize = 4493;
    internal const string ReleaseSha256 = "0754aeb36da7218d36600952be66e2eb6e78eb50f68a1e06f1ce2c99380e840e";

    private static readonly IReadOnlyDictionary<string, (string Source, string Target)> CompatibleFactoryIcons =
        new Dictionary<string, (string Source, string Target)>(StringComparer.OrdinalIgnoreCase)
        {
            ["GameData/Vehicles/Skins/BayCar_Icon_Current.dds"] = ("c3f68d125eccb3f7063a9a02ffdeed83c88049fafe01b5fb12c9f6fbedf79ea2", "e5f1578a69a9ea3e890caa52778b0c8694a5cad4f3c3393c6d7e59d5d508df7a"),
            ["GameData/Vehicles/Skins/BayCar_Icon.dds"] = ("c3f68d125eccb3f7063a9a02ffdeed83c88049fafe01b5fb12c9f6fbedf79ea2", "e5f1578a69a9ea3e890caa52778b0c8694a5cad4f3c3393c6d7e59d5d508df7a"),
            ["GameData/Vehicles/Skins/HDModelPainted_CanyonCar_Icon_Current.dds"] = ("7e37bb5032566aba8f9ab20df6101a93381742835bf07c12291822a408dbdbf0", "5d5b933783bb8a8cf361e22a956f99abe8b92314149c7d7d982f776a6b3c36eb"),
            ["GameData/Vehicles/Skins/HDModelPainted_CanyonCar_Icon.dds"] = ("7e37bb5032566aba8f9ab20df6101a93381742835bf07c12291822a408dbdbf0", "5d5b933783bb8a8cf361e22a956f99abe8b92314149c7d7d982f776a6b3c36eb"),
            ["GameData/Vehicles/Skins/CoastCar_Icon_Current.dds"] = ("40197b51d2a2f83c0cf32fc9f905ee64d121454df0167f3234939f36413b05df", "b5de33c91e86d5f3ab4e203c8326fbc7838ec1a36ecb83a95aec6905ec3258f3"),
            ["GameData/Vehicles/Skins/CoastCar_Icon.dds"] = ("40197b51d2a2f83c0cf32fc9f905ee64d121454df0167f3234939f36413b05df", "b5de33c91e86d5f3ab4e203c8326fbc7838ec1a36ecb83a95aec6905ec3258f3"),
            ["GameData/Vehicles/Skins/DesertCar_Icon_Current.dds"] = ("819f085ff90ea74fcc8c8cdc649690653191137ce301a8373e478b59d88764c2", "772685cfd27f33b73beded595cd0cb74a50f5621d582ab90859c8abc9fba32f8"),
            ["GameData/Vehicles/Skins/DesertCar_Icon.dds"] = ("819f085ff90ea74fcc8c8cdc649690653191137ce301a8373e478b59d88764c2", "772685cfd27f33b73beded595cd0cb74a50f5621d582ab90859c8abc9fba32f8"),
            ["GameData/Vehicles/Skins/IslandCar_Icon_Current.dds"] = ("d7dfec55585c5829dc574a4fe6a35349a32d936b019bcaec43822a4b05bb9f8c", "74e22ca45502e23d0fc936cb583e6d1091dcb4e39b44130917d2263eb4f50642"),
            ["GameData/Vehicles/Skins/IslandCar_Icon.dds"] = ("d7dfec55585c5829dc574a4fe6a35349a32d936b019bcaec43822a4b05bb9f8c", "74e22ca45502e23d0fc936cb583e6d1091dcb4e39b44130917d2263eb4f50642"),
            ["GameData/Vehicles/Skins/HDModelPainted_LagoonCar_Icon_Current.dds"] = ("353c22066df515c50934db04f325041d876204c9852551ec1d9a7f05dd2fa8ad", "f4505c1a1b4335e433794f829217ca5b4121c902970fbcd46fa37fec7612500c"),
            ["GameData/Vehicles/Skins/HDModelPainted_LagoonCar_Icon.dds"] = ("353c22066df515c50934db04f325041d876204c9852551ec1d9a7f05dd2fa8ad", "f4505c1a1b4335e433794f829217ca5b4121c902970fbcd46fa37fec7612500c"),
            ["GameData/Vehicles/Skins/RallyCar_Icon_Current.dds"] = ("ce84f10013f6e21440ea8b389450031b4ed3f5cc4e8c296c589c81b387f8093d", "414b517e0d721eecf1aee67d13ab675856d9b956ec5849380a5a46e255891182"),
            ["GameData/Vehicles/Skins/RallyCar_Icon.dds"] = ("ce84f10013f6e21440ea8b389450031b4ed3f5cc4e8c296c589c81b387f8093d", "414b517e0d721eecf1aee67d13ab675856d9b956ec5849380a5a46e255891182"),
            ["GameData/Vehicles/Skins/SnowCar_Icon_Current.dds"] = ("c1e20b696bae6d97562e83f5cbd21b4861f21507c1e0108a72bb48b915c7c9dd", "8e00b4b5c3e727dba3065fc3bd7cb16ce6908860a5be0265c5a032b8b8433f2d"),
            ["GameData/Vehicles/Skins/SnowCar_Icon.dds"] = ("c1e20b696bae6d97562e83f5cbd21b4861f21507c1e0108a72bb48b915c7c9dd", "8e00b4b5c3e727dba3065fc3bd7cb16ce6908860a5be0265c5a032b8b8433f2d"),
            ["GameData/Vehicles/Skins/StadiumCar_Icon_Current.dds"] = ("73038901af5ccc172d6e11f5f8c04c88b6374e5d28cc431bac0a9bc526b2eb5e", "82f28c00945ec1278711bc24b255169df367d088c48063f43c58e9fd9c93ab12"),
            ["GameData/Vehicles/Skins/StadiumCar_Icon.dds"] = ("73038901af5ccc172d6e11f5f8c04c88b6374e5d28cc431bac0a9bc526b2eb5e", "82f28c00945ec1278711bc24b255169df367d088c48063f43c58e9fd9c93ab12"),
            ["GameData/Vehicles/Skins/TrafficCar_Icon_Current.dds"] = ("76e58065f44c24b39bfa184484e69bd4c37c7bd83d5b2b7618f1392bc8f393e8", "43e247b1e676119626766a92fc68bbb287a79b2441bd07541e9fdf52e759f7cd"),
            ["GameData/Vehicles/Skins/TrafficCar_Icon.dds"] = ("76e58065f44c24b39bfa184484e69bd4c37c7bd83d5b2b7618f1392bc8f393e8", "43e247b1e676119626766a92fc68bbb287a79b2441bd07541e9fdf52e759f7cd"),
            ["GameData/Vehicles/Skins/ValleyCar_Icon_Current.dds"] = ("dd3f4c168ba593f3160920fb3eb117ec50c255be32bed713fa7e45ec8a8b36f6", "03e725c4d10b56a5bf255f4bec574d7c983981755dee11740b3d9ecdeb30bb73"),
            ["GameData/Vehicles/Skins/ValleyCar_Icon.dds"] = ("dd3f4c168ba593f3160920fb3eb117ec50c255be32bed713fa7e45ec8a8b36f6", "03e725c4d10b56a5bf255f4bec574d7c983981755dee11740b3d9ecdeb30bb73"),
        };

    internal static bool CanPreserveIconBase(string path, string source, string target) =>
        CompatibleFactoryIcons.TryGetValue(path, out var pair) &&
        source == pair.Source && target == pair.Target;
}
