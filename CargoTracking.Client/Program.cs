using CargoTracking.Grpc;
using Grpc.Net.Client;

Console.Title = "Kargo Takip İstemcisi (gRPC Client)";

// Senin çalışan 5135 portun:
using var channel = GrpcChannel.ForAddress("http://localhost:5135");
var client = new CargoService.CargoServiceClient(channel);

while (true)
{
    Console.WriteLine("\n==============================================");
    Console.WriteLine("   KARGO YÖNETİM VE TAKİP SİSTEMİ (gRPC)      ");
    Console.WriteLine("==============================================");
    Console.WriteLine("1 - Kargo Durumu Sorgula");
    Console.WriteLine("2 - Yeni Kargo Kaydı Oluştur");
    Console.WriteLine("3 - Tüm Kargoları Listele");
    Console.WriteLine("4 - Kargo Kaydı Sil");
    Console.WriteLine("Q - Çıkış");
    Console.Write("Seçiminiz: ");

    string? choice = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(choice)) continue;
    if (choice.Equals("q", StringComparison.OrdinalIgnoreCase)) break;

    try
    {
        if (choice == "1")
        {
            Console.Write("\nKargo Takip No giriniz (Örn: TR-1001): ");
            string trackingNo = Console.ReadLine()?.Trim() ?? "";

            var response = await client.GetCargoStatusAsync(new CargoLookupRequest
            {
                TrackingNumber = trackingNo
            });

            Console.WriteLine("----------------------------------------------");
            if (response.IsFound)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[KARGO BULUNDU - {response.TrackingNumber}]");
                Console.ResetColor();
                Console.WriteLine($"Gönderici        : {response.SenderName}");
                Console.WriteLine($"Alıcı            : {response.ReceiverName}");
                Console.WriteLine($"Güncel Durum     : {response.CurrentStatus}");
                Console.WriteLine($"Son Konum        : {response.LastLocation}");
                Console.WriteLine($"Tahmini Teslimat : {response.EstimatedDelivery}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BULUNAMADI]: {response.CurrentStatus}");
                Console.ResetColor();
            }
            Console.WriteLine("----------------------------------------------");
        }
        else if (choice == "2")
        {
            Console.WriteLine("\n--- YENİ KARGO KAYDI ---");
            Console.Write("Gönderici Adı Soyadı : ");
            string sender = Console.ReadLine() ?? "";

            Console.Write("Alıcı Adı Soyadı     : ");
            string receiver = Console.ReadLine() ?? "";

            Console.Write("Çıkış Şubesi / Konum : ");
            string location = Console.ReadLine() ?? "";

            Console.Write("Tahmini Teslim Tarihi: ");
            string delivery = Console.ReadLine() ?? "";

            var createResponse = await client.CreateCargoAsync(new CreateCargoRequest
            {
                SenderName = sender,
                ReceiverName = receiver,
                LastLocation = location,
                EstimatedDelivery = delivery
            });

            if (createResponse.Success)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[BAŞARILI]: {createResponse.Message}");
                Console.WriteLine($"Oluşturulan Takip Numarası: {createResponse.GeneratedTrackingNumber}");
                Console.ResetColor();
            }
        }
        else if (choice == "3")
        {
            Console.WriteLine("\n--- SİSTEMDEKİ TÜM KARGOLAR ---");
            var listResponse = await client.GetAllCargosAsync(new EmptyRequest());

            if (listResponse.Cargos.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Sistemde kayıtlı kargo bulunmamaktadır.");
                Console.ResetColor();
            }
            else
            {
                foreach (var item in listResponse.Cargos)
                {
                    Console.WriteLine($"[{item.TrackingNumber}] {item.SenderName} -> {item.ReceiverName} | Durum: {item.CurrentStatus} ({item.LastLocation})");
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nToplam {listResponse.Cargos.Count} adet kargo listelendi.");
                Console.ResetColor();
            }
        }
        else if (choice == "4")
        {
            Console.Write("\nSilmek istediğiniz Kargo Takip No: ");
            string deleteNo = Console.ReadLine()?.Trim() ?? "";

            var deleteResponse = await client.DeleteCargoAsync(new DeleteCargoRequest
            {
                TrackingNumber = deleteNo
            });

            if (deleteResponse.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SİLİNDİ]: {deleteResponse.Message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[HATA]: {deleteResponse.Message}");
            }
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Bir hata oluştu: {ex.Message}");
        Console.ResetColor();
    }
}