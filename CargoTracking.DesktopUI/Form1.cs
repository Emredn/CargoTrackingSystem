using CargoTracking.Grpc;
using Grpc.Net.Client;
using System.Data;
using System.Drawing;
using System.Globalization;

namespace CargoTracking.DesktopUI
{
    public partial class Form1 : Form
    {
        // Sunucu bağlantı adresi
        private readonly string serverAddress = "http://localhost:5135";

        // Ana Yerleşim Panelleri
        private Panel pnlSidebar = null!;
        private Panel pnlTopHeader = null!;
        private Panel pnlMain = null!;
        private Panel pnlActiveIndicator = null!; // Sol menüdeki aktif sekme çizgisi

        // Üst Bar: Ekmek Kırıntısı (Breadcrumb), Geri Butonu ve Saat
        private Button btnBack = null!;
        private Label lblBreadcrumb = null!;
        private Label lblLiveClock = null!;
        private Label lblServerStatus = null!;
        private System.Windows.Forms.Timer uiTimer = null!;

        // Sayfalar Arası Geri Dönüş Hafızası (Navigation History Stack)
        private readonly Stack<(Button MenuBtn, Panel Page, string BreadcrumbText)> _navHistory = new();
        private (Button MenuBtn, Panel Page, string BreadcrumbText) _currentNav;
        private bool _isNavigatingBack = false;

        // 4 Ana Sayfa Paneli
        private Panel pnlHome = null!;
        private Panel pnlAddCargo = null!;
        private Panel pnlQueryCargo = null!;
        private Panel pnlListCargo = null!;

        // Sol Menü Butonları
        private Button btnNavHome = null!;
        private Button btnNavAdd = null!;
        private Button btnNavQuery = null!;
        private Button btnNavList = null!;

        // 1. Sayfa (Ana Sayfa) Sayaç Etiketleri
        private Label lblTotalCount = null!;
        private Label lblActiveCount = null!;
        private Label lblPassiveCount = null!;

        // 2. Sayfa (Yeni Kargo) Kutuları
        private TextBox txtSender = null!;
        private TextBox txtReceiver = null!;
        private TextBox txtLocation = null!;
        private DateTimePicker dtpDelivery = null!;

        // 3. Sayfa (Sorgula & Düzenle) Kutuları
        private TextBox txtTrackingCode = null!;
        private Panel pnlCargoDetailCard = null!;
        private Label lblCardHeaderBadge = null!;
        private TextBox txtEditSender = null!;
        private TextBox txtEditReceiver = null!;
        private TextBox txtEditLocation = null!;
        private DateTimePicker dtpEditDelivery = null!;
        private ComboBox cmbNewStatus = null!;
        private string _currentlyQueriedTrackingNo = string.Empty;

        // 4. Sayfa (Kargo Listesi ve Sağdaki Tek Tık Özet Kartı) Bileşenleri
        private DataGridView dgvCargos = null!;
        private Label lblListTitle = null!;
        private TextBox txtLiveSearch = null!;
        private Button btnTabAll = null!;
        private Button btnTabActive = null!;
        private Button btnTabPassive = null!;
        private Panel pnlTabUnderline = null!; // Hangi sekmede olduğumuzu gösteren alt çizgi

        // MADDE 4 & 5: Tabloda 1 kez tıklayınca sağda açılan Hızlı Özet ve Kopyalama Kartı
        private Panel pnlQuickPreview = null!;
        private Label lblPreviewTrackNo = null!;
        private Label lblPreviewDetails = null!;
        private Button btnCopyTrackNo = null!;
        private Button btnGoToEdit = null!;
        private string _selectedPreviewTrackNo = string.Empty;

        private List<CargoStatusResponse> _cachedCargoList = new();
        private string _currentFilterMode = "ALL"; // "ALL", "ACTIVE", "PASSIVE"

        public Form1()
        {
            InitializeComponent();
            BuildEnterpriseUI();
        }

        // KLAVYE KORUMASI: Kullanıcı metin kutusunda değilse rastgele tuşlarla sekme değişmesini engeller
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Control? focused = this.ActiveControl;
            bool isTypingControl = focused is TextBox || focused is DateTimePicker || focused is ComboBox;

            if (!isTypingControl)
            {
                if (keyData == Keys.Tab || keyData == Keys.Space || keyData == Keys.Enter ||
                    keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Left || keyData == Keys.Right)
                {
                    return true; // Tuş vuruşunu yut
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // =========================================================================
        // ANA ARAYÜZ İNŞASI (SABİT BOYUT, KURUMSAL SLOGAN VE EKMEK KIRINTISI)
        // =========================================================================
        private async void BuildEnterpriseUI()
        {
            // MADDE 2: Ekran boyutu sabitlendi, kullanıcının pencereyi sündürüp bozması engellendi
            this.Text = "KARGO PRO | Kurumsal Lojistik ve Operasyon Yönetim Sistemi";
            this.Size = new Size(1280, 760);
            this.MinimumSize = new Size(1280, 760);
            this.MaximumSize = new Size(1280, 760);
            this.FormBorderStyle = FormBorderStyle.FixedSingle; // Kenarlardan çekip uzatmayı kapatır
            this.MaximizeBox = false; // Tam ekran yapıp oranları bozmayı kapatır
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(241, 245, 249);
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            this.KeyPreview = true;

            // --- SOL YAN MENÜ (SIDEBAR) ---
            pnlSidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 250,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            this.Controls.Add(pnlSidebar);

            // Kurumsal Başlık (Emojisiz, Ciddi Görünüm)
            Panel pnlBrandHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 95,
                BackColor = Color.FromArgb(9, 14, 26)
            };

            Label lblBrand = new Label
            {
                Text = "KARGO PRO",
                ForeColor = Color.FromArgb(56, 189, 248),
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                Location = new Point(24, 18),
                AutoSize = true
            };

            // MADDE 7: Kurumsal Slogan (Sol üst logo altı)
            Label lblBrandSlogan = new Label
            {
                Text = "Tek Merkez, Sıfır Kayıp,\nKesintisiz Lojistik Takip",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                Location = new Point(26, 52),
                AutoSize = true
            };
            pnlBrandHeader.Controls.AddRange(new Control[] { lblBrand, lblBrandSlogan });

            // Aktif menü yanındaki mavi gösterge şeridi
            pnlActiveIndicator = new Panel
            {
                Size = new Size(5, 56),
                Location = new Point(0, 105),
                BackColor = Color.FromArgb(56, 189, 248)
            };
            pnlSidebar.Controls.Add(pnlActiveIndicator);

            // MADDE 8: Emojiler kaldırıldı, kurumsal metinler eklendi
            btnNavHome = CreateMenuButton("ANA SAYFA (ÖZET)", 105);
            btnNavAdd = CreateMenuButton("YENİ KARGO KAYDI", 165);
            btnNavQuery = CreateMenuButton("SORGULA VE DÜZENLE", 225);
            btnNavList = CreateMenuButton("KARGO LİSTELERİ", 285);

            // Sol menü alt bilgi (Footer Slogan)
            Label lblFooterSlogan = new Label
            {
                Text = "Kurumsal Operasyon Ağı v2.4\nYeni Nesil Dağıtık Kargo Mimarisi",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8F),
                Dock = DockStyle.Bottom,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlSidebar.Controls.AddRange(new Control[] { pnlBrandHeader, btnNavHome, btnNavAdd, btnNavQuery, btnNavList, lblFooterSlogan });
            pnlActiveIndicator.BringToFront();

            // --- ÜST BİLGİ BARI (GERİ TUŞU + EKMEK KIRINTISI + SAAT) ---
            pnlTopHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.White,
                Padding = new Padding(20, 0, 20, 0)
            };

            // MADDE 3: Geri Gelme Tuşu
            btnBack = new Button
            {
                Text = "< Geri Dön",
                Location = new Point(20, 13),
                Size = new Size(105, 34),
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false,
                Enabled = false // İlk açılışta geçmiş olmadığı için pasif
            };
            btnBack.FlatAppearance.BorderSize = 0;
            btnBack.Click += BtnBack_Click;

            // MADDE 3: Ekmek Kırıntısı (Breadcrumb Navigation)
            lblBreadcrumb = new Label
            {
                Text = "Operasyon Merkezi  >  Ana Sayfa",
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(140, 19)
            };

            // Sunucu Durumu ve Canlı Saat
            lblServerStatus = new Label
            {
                Text = "● gRPC Sunucu: Aktif (5135)",
                ForeColor = Color.FromArgb(22, 163, 74),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(620, 20)
            };

            lblLiveClock = new Label
            {
                Text = DateTime.Now.ToString("dd.MM.yyyy  |  HH:mm:ss"),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(840, 20)
            };

            pnlTopHeader.Controls.AddRange(new Control[] { btnBack, lblBreadcrumb, lblServerStatus, lblLiveClock });
            this.Controls.Add(pnlTopHeader);
            pnlTopHeader.BringToFront();

            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += (s, e) => lblLiveClock.Text = DateTime.Now.ToString("dd.MM.yyyy  |  HH:mm:ss");
            uiTimer.Start();

            // --- SAĞ ANA İÇERİK ALANI ---
            pnlMain = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(25)
            };
            this.Controls.Add(pnlMain);
            pnlMain.BringToFront();

            // 4 Ana Sayfayı Oluştur
            CreateHomePage();
            CreateAddCargoPage();
            CreateQueryCargoPage();
            CreateListCargoPage();

            // Sol Menü Tıklama Olayları
            btnNavHome.Click += async (s, e) => { NavigateTo(btnNavHome, pnlHome, "Operasyon Merkezi  >  Ana Sayfa (Özet)"); await LoadDashboardSummaryAsync(); };
            btnNavAdd.Click += (s, e) => { NavigateTo(btnNavAdd, pnlAddCargo, "Operasyon Merkezi  >  Yeni Kargo Kaydı"); };
            btnNavQuery.Click += (s, e) => { NavigateTo(btnNavQuery, pnlQueryCargo, "Operasyon Merkezi  >  Kargo Sorgula ve Düzenle"); ResetQueryScreen(); };
            btnNavList.Click += async (s, e) => { NavigateTo(btnNavList, pnlListCargo, "Operasyon Merkezi  >  Kargo Listeleri  >  Tüm Kargolar"); await LoadCargoListAsync("ALL"); };

            // İlk Açılış
            _currentNav = (btnNavHome, pnlHome, "Operasyon Merkezi  >  Ana Sayfa (Özet)");
            ActivateMenuVisuals(btnNavHome, pnlHome, _currentNav.BreadcrumbText);
            await LoadDashboardSummaryAsync();
        }

        private Button CreateMenuButton(string text, int top)
        {
            var btn = new Button
            {
                Text = text,
                Top = top,
                Left = 0,
                Width = 250,
                Height = 56,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(24, 0, 0, 0),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59);
            return btn;
        }

        // MADDE 3: Sayfa Geçişi ve Ekmek Kırıntısı / Geri Dönüş Kaydı
        private void NavigateTo(Button targetBtn, Panel targetPage, string breadcrumbTitle)
        {
            if (!_isNavigatingBack && _currentNav.Page != null && _currentNav.Page != targetPage)
            {
                _navHistory.Push(_currentNav);
                btnBack.Enabled = true;
            }

            _currentNav = (targetBtn, targetPage, breadcrumbTitle);
            ActivateMenuVisuals(targetBtn, targetPage, breadcrumbTitle);
        }

        private async void BtnBack_Click(object? sender, EventArgs e)
        {
            if (_navHistory.Count == 0) return;

            _isNavigatingBack = true;
            var prev = _navHistory.Pop();
            _currentNav = prev;

            ActivateMenuVisuals(prev.MenuBtn, prev.Page, prev.BreadcrumbText);

            if (prev.Page == pnlHome) await LoadDashboardSummaryAsync();
            else if (prev.Page == pnlListCargo) await LoadCargoListAsync(_currentFilterMode);

            btnBack.Enabled = _navHistory.Count > 0;
            _isNavigatingBack = false;
        }

        private void ActivateMenuVisuals(Button activeBtn, Panel pageToShow, string breadcrumbText)
        {
            foreach (var btn in new[] { btnNavHome, btnNavAdd, btnNavQuery, btnNavList })
            {
                btn.BackColor = Color.FromArgb(15, 23, 42);
                btn.ForeColor = Color.FromArgb(148, 163, 184);
            }

            activeBtn.BackColor = Color.FromArgb(30, 41, 59);
            activeBtn.ForeColor = Color.White;
            pnlActiveIndicator.Top = activeBtn.Top;
            pnlActiveIndicator.BringToFront();

            lblBreadcrumb.Text = breadcrumbText;

            pnlHome.Visible = false;
            pnlAddCargo.Visible = false;
            pnlQueryCargo.Visible = false;
            pnlListCargo.Visible = false;

            pageToShow.Visible = true;
            pageToShow.BringToFront();
            pnlMain.Focus();
        }

        private void ResetQueryScreen()
        {
            txtTrackingCode.Clear();
            pnlCargoDetailCard.Visible = false;
            _currentlyQueriedTrackingNo = string.Empty;
        }

        // =========================================================================
        // 1. SAYFA: ANA SAYFA (KURUMSAL ÖZET VE SLOGAN BARI)
        // =========================================================================
        private void CreateHomePage()
        {
            pnlHome = new Panel { Dock = DockStyle.Fill, Visible = false };

            Label lblWelcome = new Label
            {
                Text = "Operasyon Kontrol Merkezi",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(20, 15)
            };

            Label lblSub = new Label
            {
                Text = "İlgili kargo listesini doğrudan görüntülemek için aşağıdaki özet kartların üzerine tıklayabilirsiniz.",
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(22, 58)
            };

            // Tıklanabilir Özet Kartları (Emojisiz, Kurumsal)
            Panel cardTotal = CreateInteractiveCard(
                "TÜM KAYITLI KARGOLAR", "Tüm listeyi açmak için tıklayın  >",
                Color.FromArgb(30, 64, 175), 20, 110, out lblTotalCount,
                async () => { NavigateTo(btnNavList, pnlListCargo, "Operasyon Merkezi  >  Kargo Listeleri  >  Tüm Kargolar"); await LoadCargoListAsync("ALL"); });

            Panel cardActive = CreateInteractiveCard(
                "AKTİF (OPERASYONDAKİ) KARGOLAR", "Sadece aktif kargoları görmek için tıklayın  >",
                Color.FromArgb(21, 128, 61), 330, 110, out lblActiveCount,
                async () => { NavigateTo(btnNavList, pnlListCargo, "Operasyon Merkezi  >  Kargo Listeleri  >  Aktif Kargolar"); await LoadCargoListAsync("ACTIVE"); });

            Panel cardPassive = CreateInteractiveCard(
                "PASİF (ARŞİVLENMİŞ) KARGOLAR", "Teslim ve iptal kayıtları için tıklayın  >",
                Color.FromArgb(71, 85, 105), 640, 110, out lblPassiveCount,
                async () => { NavigateTo(btnNavList, pnlListCargo, "Operasyon Merkezi  >  Kargo Listeleri  >  Pasif (Arşiv)"); await LoadCargoListAsync("PASSIVE"); });

            // MADDE 7: Ana Sayfa Kurumsal Slogan Banner Alanı
            Panel pnlSloganBanner = new Panel
            {
                Location = new Point(20, 310),
                Size = new Size(910, 110),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblBannerTitle = new Label
            {
                Text = "\"Hız, Güven ve Tam İzlenebilirlik: Tüm Kargo Operasyonunuz Tek Çatı Altında.\"",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold | FontStyle.Italic),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(25, 22),
                AutoSize = true
            };

            Label lblBannerDesc = new Label
            {
                Text = "KARGO PRO altyapısı ile şubeler arası gönderi takibini gerçek zamanlı yönetin, geçmişe dönük arşiv kayıtlarını sıfır veri kaybıyla denetleyin.",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(27, 60),
                AutoSize = true
            };

            pnlSloganBanner.Controls.AddRange(new Control[] { lblBannerTitle, lblBannerDesc });

            pnlHome.Controls.AddRange(new Control[] { lblWelcome, lblSub, cardTotal, cardActive, cardPassive, pnlSloganBanner });
            pnlMain.Controls.Add(pnlHome);
        }

        private Panel CreateInteractiveCard(string title, string subText, Color bgColor, int x, int y, out Label countLabel, Action onClickAction)
        {
            Panel card = new Panel
            {
                Size = new Size(290, 165),
                Location = new Point(x, y),
                BackColor = bgColor,
                Cursor = Cursors.Hand
            };

            Label lblTitle = new Label
            {
                Text = title,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(18, 18),
                AutoSize = true,
                Cursor = Cursors.Hand
            };

            countLabel = new Label
            {
                Text = "0",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 36F, FontStyle.Bold),
                Location = new Point(15, 48),
                AutoSize = true,
                Cursor = Cursors.Hand
            };

            Label lblSub = new Label
            {
                Text = subText,
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Location = new Point(18, 132),
                AutoSize = true,
                Cursor = Cursors.Hand
            };

            EventHandler clickHandler = (s, e) => onClickAction();
            card.Click += clickHandler;
            lblTitle.Click += clickHandler;
            countLabel.Click += clickHandler;
            lblSub.Click += clickHandler;

            card.Controls.AddRange(new Control[] { lblTitle, countLabel, lblSub });
            return card;
        }

        private async Task LoadDashboardSummaryAsync()
        {
            try
            {
                using var channel = GrpcChannel.ForAddress(serverAddress);
                var client = new CargoService.CargoServiceClient(channel);
                var response = await client.GetAllCargosAsync(new EmptyRequest());

                lblTotalCount.Text = response.Cargos.Count.ToString();
                lblActiveCount.Text = response.Cargos.Count(c => c.IsActive).ToString();
                lblPassiveCount.Text = response.Cargos.Count(c => !c.IsActive).ToString();
                lblServerStatus.Text = "● gRPC Sunucu: Aktif (5135)";
                lblServerStatus.ForeColor = Color.FromArgb(22, 163, 74);
            }
            catch
            {
                lblTotalCount.Text = "-";
                lblActiveCount.Text = "-";
                lblPassiveCount.Text = "-";
                lblServerStatus.Text = "● gRPC Sunucu: Bağlantı Yok";
                lblServerStatus.ForeColor = Color.Crimson;
            }
        }

        // =========================================================================
        // 2. SAYFA: YENİ KARGO GÖNDERİMİ
        // =========================================================================
        private void CreateAddCargoPage()
        {
            pnlAddCargo = new Panel { Dock = DockStyle.Fill, Visible = false };

            Label lblTitle = new Label
            {
                Text = "Yeni Kargo Gönderimi Oluştur",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(20, 15)
            };

            Panel formCard = new Panel
            {
                Size = new Size(540, 420),
                Location = new Point(20, 65),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label l1 = new Label { Text = "Gönderici Adı Soyadı / Kurum:", Location = new Point(30, 25), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            txtSender = new TextBox { Location = new Point(30, 50), Width = 460, Font = new Font("Segoe UI", 11F) };

            Label l2 = new Label { Text = "Alıcı Adı Soyadı:", Location = new Point(30, 100), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            txtReceiver = new TextBox { Location = new Point(30, 125), Width = 460, Font = new Font("Segoe UI", 11F) };

            Label l3 = new Label { Text = "Çıkış Şubesi / Son Konum:", Location = new Point(30, 175), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            txtLocation = new TextBox { Location = new Point(30, 200), Width = 460, Font = new Font("Segoe UI", 11F) };

            Label l4 = new Label { Text = "Tahmini Teslim Tarihi:", Location = new Point(30, 250), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            dtpDelivery = new DateTimePicker
            {
                Location = new Point(30, 275),
                Width = 460,
                Font = new Font("Segoe UI", 11F),
                Format = DateTimePickerFormat.Short,
                MinDate = DateTime.Today // Geçmiş tarih seçimi kilitli
            };

            Button btnSave = new Button
            {
                Text = "KARGOYU SİSTEME KAYDET",
                Location = new Point(30, 340),
                Size = new Size(460, 46),
                BackColor = Color.FromArgb(21, 128, 61),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSaveCargo_Click;

            formCard.Controls.AddRange(new Control[] { l1, txtSender, l2, txtReceiver, l3, txtLocation, l4, dtpDelivery, btnSave });
            pnlAddCargo.Controls.AddRange(new Control[] { lblTitle, formCard });
            pnlMain.Controls.Add(pnlAddCargo);
        }

        private async void BtnSaveCargo_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSender.Text) || string.IsNullOrWhiteSpace(txtReceiver.Text))
            {
                MessageBox.Show("Lütfen Gönderici ve Alıcı bilgilerini eksiksiz doldurun.", "Eksik Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                using var channel = GrpcChannel.ForAddress(serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                var response = await client.CreateCargoAsync(new CreateCargoRequest
                {
                    SenderName = txtSender.Text.Trim(),
                    ReceiverName = txtReceiver.Text.Trim(),
                    LastLocation = string.IsNullOrWhiteSpace(txtLocation.Text) ? "Merkez Transfer Şubesi" : txtLocation.Text.Trim(),
                    EstimatedDelivery = dtpDelivery.Value.ToString("dd.MM.yyyy")
                });

                if (response.Success)
                {
                    MessageBox.Show($"Kargo kaydı başarıyla oluşturuldu.\nÜretilen Takip Numarası: {response.GeneratedTrackingNumber}", "İşlem Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    txtSender.Clear();
                    txtReceiver.Clear();
                    txtLocation.Clear();
                    dtpDelivery.Value = DateTime.Today;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sunucu iletişim hatası: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // =========================================================================
        // 3. SAYFA: KARGO SORGULA VE DÜZENLE (EMOJİSİZ, KURUMSAL PANEL)
        // =========================================================================
        private void CreateQueryCargoPage()
        {
            pnlQueryCargo = new Panel { Dock = DockStyle.Fill, Visible = false };

            Label lblTitle = new Label
            {
                Text = "Kargo Sorgulama ve Kayıt Düzenleme",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(20, 15)
            };

            Panel searchBoxPanel = new Panel
            {
                Size = new Size(740, 85),
                Location = new Point(20, 65),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblInstruction = new Label
            {
                Text = "4 Haneli Kargo Takip Kodunu Girin:",
                Location = new Point(20, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.DimGray
            };

            Label lblPrefix = new Label
            {
                Text = "TR -",
                Location = new Point(20, 36),
                Size = new Size(55, 34),
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                TextAlign = ContentAlignment.MiddleRight
            };

            // 4 Hane ve Sadece Rakam Kısıtlamalı Kutu
            txtTrackingCode = new TextBox
            {
                Location = new Point(80, 36),
                Width = 120,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                MaxLength = 4,
                PlaceholderText = "1001"
            };
            txtTrackingCode.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                    e.Handled = true;
            };

            Button btnSearch = new Button
            {
                Text = "KAYDI SORGULA",
                Location = new Point(220, 34),
                Size = new Size(190, 38),
                BackColor = Color.FromArgb(30, 64, 175),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnSearch.FlatAppearance.BorderSize = 0;
            btnSearch.Click += BtnSearchCargo_Click;

            searchBoxPanel.Controls.AddRange(new Control[] { lblInstruction, lblPrefix, txtTrackingCode, btnSearch });

            // Düzenleme Kartı
            pnlCargoDetailCard = new Panel
            {
                Size = new Size(740, 370),
                Location = new Point(20, 165),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            lblCardHeaderBadge = new Label
            {
                Location = new Point(25, 18),
                Size = new Size(680, 30),
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42)
            };

            Label lblESender = new Label { Text = "Gönderici Adı Soyadı:", Location = new Point(25, 65), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            txtEditSender = new TextBox { Location = new Point(25, 90), Width = 320, Font = new Font("Segoe UI", 11F) };

            Label lblEReceiver = new Label { Text = "Alıcı Adı Soyadı:", Location = new Point(380, 65), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            txtEditReceiver = new TextBox { Location = new Point(380, 90), Width = 320, Font = new Font("Segoe UI", 11F) };

            Label lblELoc = new Label { Text = "Son Konum / Şube:", Location = new Point(25, 140), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            txtEditLocation = new TextBox { Location = new Point(25, 165), Width = 320, Font = new Font("Segoe UI", 11F) };

            Label lblEDate = new Label { Text = "Tahmini Teslim Tarihi:", Location = new Point(380, 140), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            dtpEditDelivery = new DateTimePicker
            {
                Location = new Point(380, 165),
                Width = 320,
                Font = new Font("Segoe UI", 11F),
                Format = DateTimePickerFormat.Short,
                MinDate = DateTime.Today
            };

            Label lblStatusSelect = new Label
            {
                Text = "Operasyon Durumu:",
                Location = new Point(25, 220),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            cmbNewStatus = new ComboBox
            {
                Location = new Point(25, 245),
                Width = 320,
                Font = new Font("Segoe UI", 11F),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbNewStatus.Items.AddRange(new string[] { "Hazırlanıyor", "Yola Çıktı", "Teslim Edildi", "İptal Edildi" });

            Button btnUpdateAll = new Button
            {
                Text = "DEĞİŞİKLİKLERİ VE DURUMU KAYDET",
                Location = new Point(25, 300),
                Size = new Size(675, 45),
                BackColor = Color.FromArgb(234, 88, 12),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnUpdateAll.FlatAppearance.BorderSize = 0;
            btnUpdateAll.Click += BtnUpdateStatus_Click;

            pnlCargoDetailCard.Controls.AddRange(new Control[] {
                lblCardHeaderBadge, lblESender, txtEditSender, lblEReceiver, txtEditReceiver,
                lblELoc, txtEditLocation, lblEDate, dtpEditDelivery, lblStatusSelect, cmbNewStatus, btnUpdateAll
            });

            pnlQueryCargo.Controls.AddRange(new Control[] { lblTitle, searchBoxPanel, pnlCargoDetailCard });
            pnlMain.Controls.Add(pnlQueryCargo);
        }

        private async void BtnSearchCargo_Click(object? sender, EventArgs e)
        {
            string code = txtTrackingCode.Text.Trim();
            if (code.Length != 4)
            {
                MessageBox.Show("Lütfen 4 haneli kargo kodunu eksiksiz girin (Örn: 1001).", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await QueryAndDisplayCargoAsync($"TR-{code}");
        }

        private async Task QueryAndDisplayCargoAsync(string fullTrackingNo)
        {
            try
            {
                using var channel = GrpcChannel.ForAddress(serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                var response = await client.GetCargoStatusAsync(new CargoLookupRequest { TrackingNumber = fullTrackingNo });

                if (response.IsFound)
                {
                    _currentlyQueriedTrackingNo = response.TrackingNumber;
                    string statusText = response.IsActive ? "AKTİF KAYIT" : "PASİF (ARŞİVLENMİŞ) KAYIT";
                    lblCardHeaderBadge.Text = $"Takip Numarası: {response.TrackingNumber}   |   Kayıt Statüsü: {statusText}";

                    txtEditSender.Text = response.SenderName;
                    txtEditReceiver.Text = response.ReceiverName;
                    txtEditLocation.Text = response.LastLocation;

                    if (DateTime.TryParseExact(response.EstimatedDelivery, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                        dtpEditDelivery.Value = parsedDate < DateTime.Today ? DateTime.Today : parsedDate;
                    else
                        dtpEditDelivery.Value = DateTime.Today;

                    if (cmbNewStatus.Items.Contains(response.CurrentStatus))
                        cmbNewStatus.SelectedItem = response.CurrentStatus;
                    else
                        cmbNewStatus.SelectedIndex = 0;

                    pnlCargoDetailCard.Visible = true;
                }
                else
                {
                    pnlCargoDetailCard.Visible = false;
                    MessageBox.Show($"{fullTrackingNo} numaralı kargo sistemde bulunamadı.", "Kayıt Yok", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sorgulama hatası: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BtnUpdateStatus_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentlyQueriedTrackingNo)) return;

            if (string.IsNullOrWhiteSpace(txtEditSender.Text) || string.IsNullOrWhiteSpace(txtEditReceiver.Text))
            {
                MessageBox.Show("Gönderici ve Alıcı alanları boş bırakılamaz.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string newStatus = cmbNewStatus.SelectedItem?.ToString() ?? "Hazırlanıyor";

            try
            {
                using var channel = GrpcChannel.ForAddress(serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                var response = await client.UpdateCargoStatusAsync(new UpdateStatusRequest
                {
                    TrackingNumber = _currentlyQueriedTrackingNo,
                    SenderName = txtEditSender.Text.Trim(),
                    ReceiverName = txtEditReceiver.Text.Trim(),
                    LastLocation = txtEditLocation.Text.Trim(),
                    EstimatedDelivery = dtpEditDelivery.Value.ToString("dd.MM.yyyy"),
                    NewStatus = newStatus
                });

                if (response.Success)
                {
                    MessageBox.Show(response.Message, "Güncelleme Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    // Güncelleme sonrası sorgu ekranını sıfırla
                    ResetQueryScreen();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Güncelleme hatası: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // =========================================================================
        // 4. SAYFA: KARGO LİSTELERİ + SAĞDA TEK TIK ÖZET VE KOPYALAMA KARTI
        // =========================================================================
        private void CreateListCargoPage()
        {
            pnlListCargo = new Panel { Dock = DockStyle.Fill, Visible = false };

            lblListTitle = new Label
            {
                Text = "Tüm Kargolar Listesi",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(15, 5)
            };

            // MADDE 9: "TÜM KARGOLAR" en solda, emojiler kaldırıldı ve aktif sekme vurgusu eklendi
            btnTabAll = CreateTabFilterButton("TÜM KARGOLAR", 15, 50);
            btnTabAll.Click += async (s, e) => await LoadCargoListAsync("ALL");

            btnTabActive = CreateTabFilterButton("AKTİF KARGOLAR", 195, 50);
            btnTabActive.Click += async (s, e) => await LoadCargoListAsync("ACTIVE");

            btnTabPassive = CreateTabFilterButton("PASİF (ARŞİV)", 375, 50);
            btnTabPassive.Click += async (s, e) => await LoadCargoListAsync("PASSIVE");

            // Seçili olan filtre butonunun altında duran parlak mavi vurgu çizgisi
            pnlTabUnderline = new Panel
            {
                Size = new Size(170, 4),
                Location = new Point(15, 88),
                BackColor = Color.FromArgb(56, 189, 248)
            };

            // MADDE 8: Sadece Hızlı Arama'daki büyüteç emojisi korundu
            Label lblSearchIcon = new Label
            {
                Text = "🔎 Hızlı Ara:",
                Location = new Point(575, 58),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            txtLiveSearch = new TextBox
            {
                Location = new Point(668, 55),
                Width = 290,
                Font = new Font("Segoe UI", 10.5F),
                PlaceholderText = "Takip no, gönderici, alıcı veya şube..."
            };
            txtLiveSearch.TextChanged += (s, e) => ApplyFilterAndBindGrid();

            // MADDE 2 & 4: Sabit Sütunlu, Mavi Seçim Rengi İptal Edilmiş Tablo
            dgvCargos = new DataGridView
            {
                Location = new Point(15, 105),
                Size = new Size(685, 505), // Sağdaki özet kartına yer bırakmak için sabit genişlik
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, // Sütunları biz milimetrik sabitleyeceğiz
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToResizeColumns = false, // Kullanıcı sütun genişliğiyle oynayamaz!
                AllowUserToResizeRows = false,    // Kullanıcı satır yüksekliğiyle oynayamaz!
                RowHeadersVisible = false,
                ColumnHeadersHeight = 38,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EnableHeadersVisualStyles = false, // MADDE 4: Üst başlıktaki açık mavi hover efektini kapatır!
                RowTemplate = { Height = 36 },
                TabStop = false
            };

            // Tablo üst başlık tasarımı (Koyu Kurumsal Antrasit - Tıklanınca mavi olmaz)
            dgvCargos.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 41, 59);
            dgvCargos.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvCargos.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            dgvCargos.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 41, 59);

            // Satır renklendirme ve mavi seçim rengini kendi rengine eşitleme olayı
            dgvCargos.CellFormatting += DgvCargos_CellFormatting;

            // MADDE 5: Tabloda bir satıra 1 KEZ TIKLAYINCA sağdaki özet kartını doldur
            dgvCargos.CellClick += DgvCargos_CellClick;

            // --- SAĞDAKİ HIZLI ÖZET, KOPYALAMA VE DÜZENLEMEYE GEÇİŞ KARTI (MADDE 4 & 5) ---
            pnlQuickPreview = new Panel
            {
                Location = new Point(712, 105),
                Size = new Size(255, 505),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblPreviewHeader = new Label
            {
                Text = "SEÇİLİ KARGO ÖZETİ",
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            lblPreviewTrackNo = new Label
            {
                Text = "Kargo Seçilmedi",
                Location = new Point(15, 52),
                Size = new Size(225, 28),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175)
            };

            // MADDE 4: Tek Tıkla Takip Numarası Kopyalama Butonu
            btnCopyTrackNo = new Button
            {
                Text = "TAKİP NO KOPYALA",
                Location = new Point(15, 85),
                Size = new Size(225, 32),
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false,
                Enabled = false
            };
            btnCopyTrackNo.FlatAppearance.BorderSize = 0;
            btnCopyTrackNo.Click += BtnCopyTrackNo_Click;

            lblPreviewDetails = new Label
            {
                Text = "Detayları görmek, takip numarasını kopyalamak veya düzenlemek için soldaki listeden bir kargoya tıklayın.",
                Location = new Point(15, 132),
                Size = new Size(225, 290),
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            // MADDE 5: En altta Düzenleme Ekranına Götüren Buton
            btnGoToEdit = new Button
            {
                Text = "BU KARGOYU DÜZENLE  >",
                Location = new Point(15, 440),
                Size = new Size(225, 44),
                BackColor = Color.FromArgb(234, 88, 12),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false,
                Enabled = false
            };
            btnGoToEdit.FlatAppearance.BorderSize = 0;
            btnGoToEdit.Click += async (s, e) =>
            {
                if (string.IsNullOrEmpty(_selectedPreviewTrackNo)) return;
                string digitsOnly = _selectedPreviewTrackNo.Replace("TR-", "");

                NavigateTo(btnNavQuery, pnlQueryCargo, $"Operasyon Merkezi  >  Düzenle ({_selectedPreviewTrackNo})");
                txtTrackingCode.Text = digitsOnly;
                await QueryAndDisplayCargoAsync(_selectedPreviewTrackNo);
            };

            pnlQuickPreview.Controls.AddRange(new Control[] { lblPreviewHeader, lblPreviewTrackNo, btnCopyTrackNo, lblPreviewDetails, btnGoToEdit });

            pnlListCargo.Controls.AddRange(new Control[] {
                lblListTitle, btnTabAll, btnTabActive, btnTabPassive, pnlTabUnderline,
                lblSearchIcon, txtLiveSearch, dgvCargos, pnlQuickPreview
            });
            pnlMain.Controls.Add(pnlListCargo);
        }

        private Button CreateTabFilterButton(string text, int x, int y)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(170, 38),
                BackColor = Color.FromArgb(203, 213, 225),
                ForeColor = Color.FromArgb(51, 65, 85),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        // MADDE 9: Hangi Kargo Listesi Sekmesindeysek Onu Parlat, Diğerlerini Soldur
        private void HighlightActiveTabButton(string mode)
        {
            foreach (var b in new[] { btnTabAll, btnTabActive, btnTabPassive })
            {
                b.BackColor = Color.FromArgb(226, 232, 240); // Pasif sekme rengi (Açık Gri)
                b.ForeColor = Color.FromArgb(100, 116, 139);
            }

            Button activeBtn = mode switch
            {
                "ACTIVE" => btnTabActive,
                "PASSIVE" => btnTabPassive,
                _ => btnTabAll
            };

            // Seçili sekmeyi koyu lacivert yap ve alt çizgiyi onun altına kaydır
            activeBtn.BackColor = Color.FromArgb(15, 23, 42);
            activeBtn.ForeColor = Color.White;
            pnlTabUnderline.Left = activeBtn.Left;
            pnlTabUnderline.BringToFront();
        }

        private async Task LoadCargoListAsync(string mode)
        {
            _currentFilterMode = mode;
            HighlightActiveTabButton(mode);

            string subName = mode switch
            {
                "ACTIVE" => "Aktif Kargolar",
                "PASSIVE" => "Pasif (Arşiv) Kargolar",
                _ => "Tüm Kargolar"
            };
            lblListTitle.Text = $"{subName} Listesi";
            lblBreadcrumb.Text = $"Operasyon Merkezi  >  Kargo Listeleri  >  {subName}";

            try
            {
                using var channel = GrpcChannel.ForAddress(serverAddress);
                var client = new CargoService.CargoServiceClient(channel);

                var response = await client.GetAllCargosAsync(new EmptyRequest());
                _cachedCargoList = response.Cargos.ToList();

                ApplyFilterAndBindGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Liste yüklenemedi: {ex.Message}", "Bağlantı Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // MADDE 2: Tablo Verilerini Bağlama ve Sütun Genişliklerini Milimetrik Sabitleme
        private void ApplyFilterAndBindGrid()
        {
            IEnumerable<CargoStatusResponse> query = _cachedCargoList;

            if (_currentFilterMode == "ACTIVE")
                query = query.Where(c => c.IsActive);
            else if (_currentFilterMode == "PASSIVE")
                query = query.Where(c => !c.IsActive);

            string search = txtLiveSearch.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c =>
                    c.TrackingNumber.ToLower().Contains(search) ||
                    c.SenderName.ToLower().Contains(search) ||
                    c.ReceiverName.ToLower().Contains(search) ||
                    c.LastLocation.ToLower().Contains(search));
            }

            var displayList = query.Select(c => new
            {
                TakipNo = c.TrackingNumber,
                Gönderici = c.SenderName,
                Alıcı = c.ReceiverName,
                Durum = c.CurrentStatus,
                SonKonum = c.LastLocation,
                TeslimTarihi = c.EstimatedDelivery
            }).ToList();

            dgvCargos.DataSource = displayList;

            // Sütunların sağdan taşmaması için toplam 680px genişliğe tam oturacak şekilde kilitliyoruz
            if (dgvCargos.Columns.Count >= 6)
            {
                dgvCargos.Columns["TakipNo"].HeaderText = "Takip No";
                dgvCargos.Columns["TakipNo"].Width = 80;

                dgvCargos.Columns["Gönderici"].HeaderText = "Gönderici";
                dgvCargos.Columns["Gönderici"].Width = 115;

                dgvCargos.Columns["Alıcı"].HeaderText = "Alıcı";
                dgvCargos.Columns["Alıcı"].Width = 115;

                dgvCargos.Columns["Durum"].HeaderText = "Güncel Durum";
                dgvCargos.Columns["Durum"].Width = 115;

                dgvCargos.Columns["SonKonum"].HeaderText = "Son Konum / Şube";
                dgvCargos.Columns["SonKonum"].Width = 160;

                dgvCargos.Columns["TeslimTarihi"].HeaderText = "Teslim Tarihi";
                dgvCargos.Columns["TeslimTarihi"].Width = 95;
            }

            // Eğer listede kayıt varsa ilk satırı otomatik sağdaki özet kartında göster
            if (displayList.Count > 0)
                UpdateQuickPreviewCard(displayList[0].TakipNo);
            else
                ClearQuickPreviewCard();
        }

        // MADDE 5: Tabloda bir satıra 1 kez tıklanınca sağdaki Özet Kartını güncelle
        private void DgvCargos_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dgvCargos.Rows[e.RowIndex].Cells["TakipNo"].Value != null)
            {
                string trackNo = dgvCargos.Rows[e.RowIndex].Cells["TakipNo"].Value.ToString()!;
                UpdateQuickPreviewCard(trackNo);
            }
        }

        private void UpdateQuickPreviewCard(string trackNo)
        {
            var cargo = _cachedCargoList.FirstOrDefault(c => c.TrackingNumber == trackNo);
            if (cargo == null) return;

            _selectedPreviewTrackNo = cargo.TrackingNumber;
            lblPreviewTrackNo.Text = cargo.TrackingNumber;

            string statuText = cargo.IsActive ? "Aktif (Operasyonda)" : "Pasif (Arşivde)";
            lblPreviewDetails.Text =
                $"──────────────────────────\n" +
                $"GÖNDERİCİ BİLGİSİ:\n{cargo.SenderName}\n\n" +
                $"ALICI BİLGİSİ:\n{cargo.ReceiverName}\n\n" +
                $"OPERASYON DURUMU:\n{cargo.CurrentStatus}\n\n" +
                $"SON KONUM / ŞUBE:\n{cargo.LastLocation}\n\n" +
                $"TAHMİNİ TESLİM TARİHİ:\n{cargo.EstimatedDelivery}\n\n" +
                $"KAYIT STATÜSÜ:\n{statuText}";

            btnCopyTrackNo.Enabled = true;
            btnGoToEdit.Enabled = true;
        }

        private void ClearQuickPreviewCard()
        {
            _selectedPreviewTrackNo = string.Empty;
            lblPreviewTrackNo.Text = "Kayıt Yok";
            lblPreviewDetails.Text = "Bu listede görüntülenecek kargo kaydı bulunmuyor.";
            btnCopyTrackNo.Enabled = false;
            btnGoToEdit.Enabled = false;
        }

        // MADDE 4: Takip Numarasını Panoya (Clipboard) Kopyalama Şovu
        private async void BtnCopyTrackNo_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPreviewTrackNo)) return;

            Clipboard.SetText(_selectedPreviewTrackNo);

            // Görsel Geri Bildirim: Buton 1.2 saniyeliğine yeşil olup "KOPYALANDI" yazar
            string originalText = btnCopyTrackNo.Text;
            Color originalBg = btnCopyTrackNo.BackColor;

            btnCopyTrackNo.Text = "✓ PANOYA KOPYALANDI!";
            btnCopyTrackNo.BackColor = Color.FromArgb(22, 163, 74);
            btnCopyTrackNo.ForeColor = Color.White;

            await Task.Delay(1200);

            btnCopyTrackNo.Text = originalText;
            btnCopyTrackNo.BackColor = originalBg;
            btnCopyTrackNo.ForeColor = Color.FromArgb(15, 23, 42);
        }

        // MADDE 4: Tıklanan satırın mavi olmasını engeller, kendi durum renginde kalmasını sağlar!
        private void DgvCargos_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || dgvCargos.Rows[e.RowIndex].Cells["Durum"].Value == null) return;

            string status = dgvCargos.Rows[e.RowIndex].Cells["Durum"].Value.ToString()!;
            DataGridViewRow row = dgvCargos.Rows[e.RowIndex];

            Color bgColor;
            Color fgColor;

            if (status == "Teslim Edildi")
            {
                bgColor = Color.FromArgb(220, 252, 231); // Pastel Yeşil
                fgColor = Color.FromArgb(20, 83, 45);
            }
            else if (status == "İptal Edildi")
            {
                bgColor = Color.FromArgb(254, 226, 226); // Pastel Kırmızı
                fgColor = Color.FromArgb(127, 29, 29);
            }
            else if (status == "Yola Çıktı")
            {
                bgColor = Color.FromArgb(224, 242, 254); // Pastel Mavi
                fgColor = Color.FromArgb(12, 74, 110);
            }
            else
            {
                bgColor = Color.FromArgb(254, 249, 195); // Hazırlanıyor - Pastel Sarı
                fgColor = Color.FromArgb(113, 63, 18);
            }

            // Hem normal rengi hem de seçildiğindeki (Selection) rengi aynı yapıyoruz ki mavi olmasın!
            row.DefaultCellStyle.BackColor = bgColor;
            row.DefaultCellStyle.ForeColor = fgColor;
            row.DefaultCellStyle.SelectionBackColor = bgColor;
            row.DefaultCellStyle.SelectionForeColor = fgColor;
        }
    }
}