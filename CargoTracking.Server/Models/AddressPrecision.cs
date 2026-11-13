namespace CargoTracking.Server.Models
{
    /// <summary>
    /// Teslimat adresinin haritada ne kadar kesin bulunabildiğini belirtir.
    /// Street en iyi sonuçtur; City en kaba sonuçtur (il merkezine yönlendirilmiştir).
    /// </summary>
    public enum AddressPrecision
    {
        Street,
        Neighborhood,
        District,
        City
    }
}
