namespace AllMCPSolution.Services;

public static class GeoCoordinatesService
{
     public static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> RegionCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bordeaux"] = (-0.58, 44.84),
            ["Burgundy"] = (4.75, 47.0),
            ["Champagne"] = (4.05, 49.05),
            ["Rhône"] = (4.8, 45.0),
            ["Rhone"] = (4.8, 45.0),
            ["Loire"] = (-0.5, 47.5),
            ["Provence"] = (6.2, 43.5),
            ["Tuscany"] = (11.0, 43.4),
            ["Piedmont"] = (8.0, 44.7),
            ["Veneto"] = (11.5, 45.5),
            ["Ribera del Duero"] = (-3.75, 41.7),
            ["Ribera Del Duero"] = (-3.75, 41.7),
            ["Rioja"] = (-2.43, 42.4),
            ["Douro"] = (-7.8, 41.1),
            ["Douro Valley"] = (-7.8, 41.1),
            ["Mosel"] = (6.7, 49.8),
            ["Rheingau"] = (8.0, 50.0),
            ["Nahe"] = (7.75, 49.8),
            ["Finger Lakes"] = (-76.9, 42.7),
            ["Napa Valley"] = (-122.3, 38.5),
            ["Sonoma"] = (-122.5, 38.3),
            ["Willamette Valley"] = (-123.0, 45.2),
            ["Columbia Valley"] = (-119.5, 46.2),
            ["Marlborough"] = (173.9, -41.5),
            ["Central Otago"] = (169.2, -45.0),
            ["Barossa"] = (138.95, -34.5),
            ["McLaren Vale"] = (138.5, -35.2),
            ["Mc Laren Vale"] = (138.5, -35.2),
            ["Yarra Valley"] = (145.5, -37.7),
            ["Coonawarra"] = (140.8, -37.3),
            ["Maipo"] = (-70.55, -33.6),
            ["Maipo Valley"] = (-70.55, -33.6),
            ["Mendoza"] = (-68.85, -32.9),
            ["Mendoza Valley"] = (-68.85, -32.9),
            ["Stellenbosch"] = (18.86, -33.9)
        };

    public static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> CountryCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["France"] = (2.21, 46.23),
            ["Italy"] = (12.57, 41.87),
            ["Spain"] = (-3.75, 40.46),
            ["Portugal"] = (-8.0, 39.69),
            ["Germany"] = (10.45, 51.17),
            ["Austria"] = (14.55, 47.52),
            ["Switzerland"] = (8.23, 46.82),
            ["United States"] = (-98.58, 39.83),
            ["United States of America"] = (-98.58, 39.83),
            ["USA"] = (-98.58, 39.83),
            ["U.S.A."] = (-98.58, 39.83),
            ["US"] = (-98.58, 39.83),
            ["Canada"] = (-106.35, 56.13),
            ["Chile"] = (-70.67, -33.45),
            ["Argentina"] = (-63.62, -38.42),
            ["Australia"] = (133.78, -25.27),
            ["New Zealand"] = (174.78, -41.28),
            ["South Africa"] = (22.94, -30.56),
            ["England"] = (-1.17, 52.36),
            ["United Kingdom"] = (-3.44, 55.38),
            ["UK"] = (-3.44, 55.38),
            ["Scotland"] = (-4.2, 56.82),
            ["Ireland"] = (-8.0, 53.41),
            ["Japan"] = (138.25, 36.2),
            ["China"] = (104.2, 35.86),
            ["Georgia"] = (43.36, 42.32),
            ["Greece"] = (22.0, 39.07),
            ["Hungary"] = (19.5, 47.16),
            ["Slovenia"] = (14.82, 46.15),
            ["Croatia"] = (15.2, 45.1),
            ["Uruguay"] = (-55.77, -32.52)
        };
}