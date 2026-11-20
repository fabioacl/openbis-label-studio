#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace OpenBISLabelStudio
{
    partial class SetupSettingsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposePreviewCache();
                components?.Dispose();
            }

            base.Dispose(disposing);

            if (disposing)
            {
                DisposeOwnedFonts();
            }
        }

        partial void DisposePreviewCache();
        partial void DisposeOwnedFonts();

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            pnlTopBar = new Panel();
            pnlWizardHeader = new Panel();
            lblWizardTitle = new Label();
            lblWizardSubtitle = new Label();
            pnlSettingsTabs = new Panel();
            btnTabConnection = new Button();
            btnTabObjectTypes = new Button();
            btnTabPrinters = new Button();
            pnlTopDivider = new Panel();
            pnlBottomBar = new Panel();
            pnlBottomDivider = new Panel();
            lblFooterError = new Label();
            btnBack = new Button();
            btnNextFinish = new Button();
            btnCancel = new Button();
            pnlContentHost = new Panel();
            pnlConnection = new Panel();
            lblConnSectionTitle = new Label();
            lblConnHint = new Label();
            lblInstanceUrl = new Label();
            txtInstanceUrl = new TextBox();
            lblToken = new Label();
            txtToken = new TextBox();
            chkShowToken = new CheckBox();
            btnTestConnection = new Button();
            prgConnectionLoading = new ProgressBar();
            lblConnectionStatus = new Label();
            pnlObjectTypes = new Panel();
            lblTypeSearch = new Label();
            txtTypeFilter = new TextBox();
            btnRefreshTypes = new Button();
            prgTypesLoading = new ProgressBar();
            lblTypesStatus = new Label();
            dgvObjectTypes = new DataGridView();
            colUse = new DataGridViewCheckBoxColumn();
            colType = new DataGridViewTextBoxColumn();
            colStyle = new DataGridViewComboBoxColumn();
            grpExamplePreviews = new GroupBox();
            btnPreviewMolecule = new Button();
            btnPreviewGeneric = new Button();
            lblPreviewActiveHint = new Label();
            lblPreviewSmallBoxTitle = new Label();
            picPreviewSmallBox = new PictureBox();
            lblPreviewMagnetTitle = new Label();
            picPreviewMagnet = new PictureBox();
            lblPreviewFlaskTitle = new Label();
            picPreviewFlask = new PictureBox();
            pnlPrinters = new Panel();
            lblPrintersSectionTitle = new Label();
            lblPrintersHint = new Label();
            lblNoPrintersWarning = new Label();
            lblSmallBoxPrinter = new Label();
            cmbSmallBoxPrinter = new ComboBox();
            lblMagnetPrinter = new Label();
            cmbMagnetPrinter = new ComboBox();
            lblFlaskPrinter = new Label();
            cmbFlaskPrinter = new ComboBox();

            pnlTopBar.SuspendLayout();
            pnlWizardHeader.SuspendLayout();
            pnlSettingsTabs.SuspendLayout();
            pnlBottomBar.SuspendLayout();
            pnlContentHost.SuspendLayout();
            pnlConnection.SuspendLayout();
            pnlObjectTypes.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvObjectTypes).BeginInit();
            grpExamplePreviews.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picPreviewSmallBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)picPreviewMagnet).BeginInit();
            ((System.ComponentModel.ISupportInitialize)picPreviewFlask).BeginInit();
            pnlPrinters.SuspendLayout();
            SuspendLayout();

            // 
            // pnlTopBar
            // 
            pnlTopBar.BackColor = Color.FromArgb(245, 247, 250);
            pnlTopBar.Controls.Add(pnlWizardHeader);
            pnlTopBar.Controls.Add(pnlSettingsTabs);
            pnlTopBar.Controls.Add(pnlTopDivider);
            pnlTopBar.Dock = DockStyle.Top;
            pnlTopBar.Location = new Point(0, 0);
            pnlTopBar.Name = "pnlTopBar";
            pnlTopBar.Size = new Size(940, 64);
            pnlTopBar.TabIndex = 0;

            // 
            // pnlWizardHeader
            // 
            pnlWizardHeader.BackColor = Color.FromArgb(245, 247, 250);
            pnlWizardHeader.Controls.Add(lblWizardTitle);
            pnlWizardHeader.Controls.Add(lblWizardSubtitle);
            pnlWizardHeader.Dock = DockStyle.Fill;
            pnlWizardHeader.Location = new Point(0, 0);
            pnlWizardHeader.Name = "pnlWizardHeader";
            pnlWizardHeader.Size = new Size(940, 63);
            pnlWizardHeader.TabIndex = 0;

            // 
            // lblWizardTitle
            // 
            lblWizardTitle.AutoSize = true;
            lblWizardTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            lblWizardTitle.ForeColor = Color.FromArgb(24, 34, 48);
            lblWizardTitle.Location = new Point(24, 9);
            lblWizardTitle.Name = "lblWizardTitle";
            lblWizardTitle.Size = new Size(280, 21);
            lblWizardTitle.TabIndex = 0;
            lblWizardTitle.Text = "Step 1 of 3 — Connect to openBIS";
            lblWizardTitle.UseMnemonic = false;

            // 
            // lblWizardSubtitle
            // 
            lblWizardSubtitle.AutoSize = true;
            lblWizardSubtitle.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblWizardSubtitle.ForeColor = Color.DimGray;
            lblWizardSubtitle.Location = new Point(24, 36);
            lblWizardSubtitle.Name = "lblWizardSubtitle";
            lblWizardSubtitle.Size = new Size(340, 15);
            lblWizardSubtitle.TabIndex = 1;
            lblWizardSubtitle.Text = "Enter your openBIS server URL and personal access token.";
            lblWizardSubtitle.UseMnemonic = false;

            // 
            // pnlSettingsTabs
            // 
            pnlSettingsTabs.BackColor = Color.FromArgb(245, 247, 250);
            pnlSettingsTabs.Controls.Add(btnTabConnection);
            pnlSettingsTabs.Controls.Add(btnTabObjectTypes);
            pnlSettingsTabs.Controls.Add(btnTabPrinters);
            pnlSettingsTabs.Dock = DockStyle.Fill;
            pnlSettingsTabs.Location = new Point(0, 0);
            pnlSettingsTabs.Name = "pnlSettingsTabs";
            pnlSettingsTabs.Size = new Size(940, 63);
            pnlSettingsTabs.TabIndex = 1;
            pnlSettingsTabs.Visible = false;

            // 
            // btnTabConnection
            // 
            btnTabConnection.FlatStyle = FlatStyle.Flat;
            btnTabConnection.Location = new Point(24, 14);
            btnTabConnection.Name = "btnTabConnection";
            btnTabConnection.Size = new Size(280, 36);
            btnTabConnection.TabIndex = 0;
            btnTabConnection.Text = "1. Connection";
            btnTabConnection.UseMnemonic = false;
            btnTabConnection.UseVisualStyleBackColor = false;
            btnTabConnection.Click += btnTabConnection_Click;

            // 
            // btnTabObjectTypes
            // 
            btnTabObjectTypes.FlatStyle = FlatStyle.Flat;
            btnTabObjectTypes.Location = new Point(320, 14);
            btnTabObjectTypes.Name = "btnTabObjectTypes";
            btnTabObjectTypes.Size = new Size(300, 36);
            btnTabObjectTypes.TabIndex = 1;
            btnTabObjectTypes.Text = "2. Object Types & Templates";
            btnTabObjectTypes.UseMnemonic = false;
            btnTabObjectTypes.UseVisualStyleBackColor = false;
            btnTabObjectTypes.Click += btnTabObjectTypes_Click;

            // 
            // btnTabPrinters
            // 
            btnTabPrinters.FlatStyle = FlatStyle.Flat;
            btnTabPrinters.Location = new Point(636, 14);
            btnTabPrinters.Name = "btnTabPrinters";
            btnTabPrinters.Size = new Size(280, 36);
            btnTabPrinters.TabIndex = 2;
            btnTabPrinters.Text = "3. Printer Routing";
            btnTabPrinters.UseMnemonic = false;
            btnTabPrinters.UseVisualStyleBackColor = false;
            btnTabPrinters.Click += btnTabPrinters_Click;

            // 
            // pnlTopDivider
            // 
            pnlTopDivider.BackColor = Color.Gainsboro;
            pnlTopDivider.Dock = DockStyle.Bottom;
            pnlTopDivider.Location = new Point(0, 63);
            pnlTopDivider.Name = "pnlTopDivider";
            pnlTopDivider.Size = new Size(940, 1);
            pnlTopDivider.TabIndex = 2;

            // 
            // pnlBottomBar
            // 
            pnlBottomBar.BackColor = Color.FromArgb(248, 249, 250);
            pnlBottomBar.Controls.Add(pnlBottomDivider);
            pnlBottomBar.Controls.Add(lblFooterError);
            pnlBottomBar.Controls.Add(btnBack);
            pnlBottomBar.Controls.Add(btnNextFinish);
            pnlBottomBar.Controls.Add(btnCancel);
            pnlBottomBar.Dock = DockStyle.Bottom;
            pnlBottomBar.Location = new Point(0, 616);
            pnlBottomBar.Name = "pnlBottomBar";
            pnlBottomBar.Size = new Size(940, 64);
            pnlBottomBar.TabIndex = 2;

            // 
            // pnlBottomDivider
            // 
            pnlBottomDivider.BackColor = Color.Gainsboro;
            pnlBottomDivider.Dock = DockStyle.Top;
            pnlBottomDivider.Location = new Point(0, 0);
            pnlBottomDivider.Name = "pnlBottomDivider";
            pnlBottomDivider.Size = new Size(940, 1);
            pnlBottomDivider.TabIndex = 0;

            // 
            // lblFooterError
            // 
            lblFooterError.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblFooterError.AutoEllipsis = true;
            lblFooterError.ForeColor = Color.Firebrick;
            lblFooterError.Location = new Point(24, 18);
            lblFooterError.Name = "lblFooterError";
            lblFooterError.Size = new Size(530, 28);
            lblFooterError.TabIndex = 1;
            lblFooterError.TextAlign = ContentAlignment.MiddleLeft;

            // 
            // btnBack
            // 
            btnBack.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnBack.Location = new Point(568, 14);
            btnBack.Name = "btnBack";
            btnBack.Size = new Size(108, 36);
            btnBack.TabIndex = 2;
            btnBack.Text = "< Back";
            btnBack.UseVisualStyleBackColor = true;
            btnBack.Click += btnBack_Click;

            // 
            // btnNextFinish
            // 
            btnNextFinish.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnNextFinish.Location = new Point(686, 14);
            btnNextFinish.Name = "btnNextFinish";
            btnNextFinish.Size = new Size(120, 36);
            btnNextFinish.TabIndex = 3;
            btnNextFinish.Text = "Next >";
            btnNextFinish.UseVisualStyleBackColor = true;
            btnNextFinish.Click += btnNextFinish_Click;

            // 
            // btnCancel
            // 
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(816, 14);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(100, 36);
            btnCancel.TabIndex = 4;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.Click += btnCancel_Click;

            // 
            // pnlContentHost
            // 
            pnlContentHost.Controls.Add(pnlConnection);
            pnlContentHost.Controls.Add(pnlObjectTypes);
            pnlContentHost.Controls.Add(pnlPrinters);
            pnlContentHost.Dock = DockStyle.Fill;
            pnlContentHost.Location = new Point(0, 64);
            pnlContentHost.Name = "pnlContentHost";
            pnlContentHost.Size = new Size(940, 552);
            pnlContentHost.TabIndex = 1;

            // 
            // pnlConnection
            // 
            pnlConnection.Controls.Add(lblConnSectionTitle);
            pnlConnection.Controls.Add(lblConnHint);
            pnlConnection.Controls.Add(lblInstanceUrl);
            pnlConnection.Controls.Add(txtInstanceUrl);
            pnlConnection.Controls.Add(lblToken);
            pnlConnection.Controls.Add(txtToken);
            pnlConnection.Controls.Add(chkShowToken);
            pnlConnection.Controls.Add(btnTestConnection);
            pnlConnection.Controls.Add(prgConnectionLoading);
            pnlConnection.Controls.Add(lblConnectionStatus);
            pnlConnection.Dock = DockStyle.Fill;
            pnlConnection.Location = new Point(0, 0);
            pnlConnection.Name = "pnlConnection";
            pnlConnection.Size = new Size(940, 552);
            pnlConnection.TabIndex = 0;

            // 
            // lblConnSectionTitle
            // 
            lblConnSectionTitle.AutoSize = true;
            lblConnSectionTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            lblConnSectionTitle.Location = new Point(36, 28);
            lblConnSectionTitle.Name = "lblConnSectionTitle";
            lblConnSectionTitle.Size = new Size(210, 20);
            lblConnSectionTitle.TabIndex = 0;
            lblConnSectionTitle.Text = "openBIS Server Credentials";

            // 
            // lblConnHint
            // 
            lblConnHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblConnHint.ForeColor = Color.DimGray;
            lblConnHint.Location = new Point(36, 56);
            lblConnHint.Name = "lblConnHint";
            lblConnHint.Size = new Size(868, 22);
            lblConnHint.TabIndex = 1;
            lblConnHint.Text = "Provide your openBIS instance URL and a valid Personal Access Token (PAT).";

            // 
            // lblInstanceUrl
            // 
            lblInstanceUrl.AutoSize = true;
            lblInstanceUrl.Location = new Point(36, 102);
            lblInstanceUrl.Name = "lblInstanceUrl";
            lblInstanceUrl.Size = new Size(80, 15);
            lblInstanceUrl.TabIndex = 2;
            lblInstanceUrl.Text = "Instance URL:";

            // 
            // txtInstanceUrl
            // 
            txtInstanceUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtInstanceUrl.Location = new Point(36, 126);
            txtInstanceUrl.Name = "txtInstanceUrl";
            txtInstanceUrl.PlaceholderText = "https://openbis.your-institution.ch";
            txtInstanceUrl.Size = new Size(868, 27);
            txtInstanceUrl.TabIndex = 3;
            txtInstanceUrl.TextChanged += ConnectionInput_TextChanged;

            // 
            // lblToken
            // 
            lblToken.AutoSize = true;
            lblToken.Location = new Point(36, 174);
            lblToken.Name = "lblToken";
            lblToken.Size = new Size(134, 15);
            lblToken.TabIndex = 4;
            lblToken.Text = "Personal Access Token:";

            // 
            // txtToken
            // 
            txtToken.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtToken.Location = new Point(36, 198);
            txtToken.Name = "txtToken";
            txtToken.PlaceholderText = "Paste openBIS personal access token...";
            txtToken.Size = new Size(868, 27);
            txtToken.TabIndex = 5;
            txtToken.UseSystemPasswordChar = true;
            txtToken.TextChanged += ConnectionInput_TextChanged;

            // 
            // chkShowToken
            // 
            chkShowToken.AutoSize = true;
            chkShowToken.Location = new Point(36, 234);
            chkShowToken.Name = "chkShowToken";
            chkShowToken.Size = new Size(92, 19);
            chkShowToken.TabIndex = 6;
            chkShowToken.Text = "Show token";
            chkShowToken.UseVisualStyleBackColor = true;
            chkShowToken.CheckedChanged += chkShowToken_CheckedChanged;

            // 
            // btnTestConnection
            // 
            btnTestConnection.Location = new Point(36, 276);
            btnTestConnection.Name = "btnTestConnection";
            btnTestConnection.Size = new Size(165, 36);
            btnTestConnection.TabIndex = 7;
            btnTestConnection.Text = "Test Connection";
            btnTestConnection.UseVisualStyleBackColor = true;
            btnTestConnection.Click += btnTestConnection_Click;

            // 
            // prgConnectionLoading
            // 
            prgConnectionLoading.Location = new Point(215, 286);
            prgConnectionLoading.MarqueeAnimationSpeed = 25;
            prgConnectionLoading.Name = "prgConnectionLoading";
            prgConnectionLoading.Size = new Size(180, 16);
            prgConnectionLoading.Style = ProgressBarStyle.Marquee;
            prgConnectionLoading.TabIndex = 9;
            prgConnectionLoading.Visible = false;

            // 
            // lblConnectionStatus
            // 
            lblConnectionStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblConnectionStatus.Location = new Point(36, 330);
            lblConnectionStatus.Name = "lblConnectionStatus";
            lblConnectionStatus.Size = new Size(868, 120);
            lblConnectionStatus.TabIndex = 8;

            // 
            // pnlObjectTypes
            // 
            pnlObjectTypes.Controls.Add(lblTypeSearch);
            pnlObjectTypes.Controls.Add(txtTypeFilter);
            pnlObjectTypes.Controls.Add(btnRefreshTypes);
            pnlObjectTypes.Controls.Add(prgTypesLoading);
            pnlObjectTypes.Controls.Add(lblTypesStatus);
            pnlObjectTypes.Controls.Add(dgvObjectTypes);
            pnlObjectTypes.Controls.Add(grpExamplePreviews);
            pnlObjectTypes.Dock = DockStyle.Fill;
            pnlObjectTypes.Location = new Point(0, 0);
            pnlObjectTypes.Name = "pnlObjectTypes";
            pnlObjectTypes.Size = new Size(940, 552);
            pnlObjectTypes.TabIndex = 1;
            pnlObjectTypes.Visible = false;

            // 
            // lblTypeSearch
            // 
            lblTypeSearch.AutoSize = true;
            lblTypeSearch.Location = new Point(24, 17);
            lblTypeSearch.Name = "lblTypeSearch";
            lblTypeSearch.Size = new Size(37, 15);
            lblTypeSearch.TabIndex = 0;
            lblTypeSearch.Text = "Filter:";

            // 
            // txtTypeFilter
            // 
            txtTypeFilter.Location = new Point(72, 13);
            txtTypeFilter.Name = "txtTypeFilter";
            txtTypeFilter.PlaceholderText = "Search by code or description...";
            txtTypeFilter.Size = new Size(340, 27);
            txtTypeFilter.TabIndex = 1;
            txtTypeFilter.TextChanged += txtTypeFilter_TextChanged;

            // 
            // btnRefreshTypes
            // 
            btnRefreshTypes.Location = new Point(424, 12);
            btnRefreshTypes.Name = "btnRefreshTypes";
            btnRefreshTypes.Size = new Size(130, 29);
            btnRefreshTypes.TabIndex = 2;
            btnRefreshTypes.Text = "Refresh Types";
            btnRefreshTypes.UseVisualStyleBackColor = true;
            btnRefreshTypes.Click += btnRefreshTypes_Click;

            // 
            // prgTypesLoading
            // 
            prgTypesLoading.Location = new Point(568, 19);
            prgTypesLoading.MarqueeAnimationSpeed = 25;
            prgTypesLoading.Name = "prgTypesLoading";
            prgTypesLoading.Size = new Size(180, 16);
            prgTypesLoading.Style = ProgressBarStyle.Marquee;
            prgTypesLoading.TabIndex = 6;
            prgTypesLoading.Visible = false;

            // 
            // lblTypesStatus
            // 
            lblTypesStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblTypesStatus.AutoEllipsis = true;
            lblTypesStatus.Location = new Point(24, 46);
            lblTypesStatus.Name = "lblTypesStatus";
            lblTypesStatus.Size = new Size(892, 24);
            lblTypesStatus.TabIndex = 3;
            lblTypesStatus.TextAlign = ContentAlignment.MiddleLeft;

            // 
            // dgvObjectTypes
            // 
            dgvObjectTypes.AllowUserToAddRows = false;
            dgvObjectTypes.AllowUserToDeleteRows = false;
            dgvObjectTypes.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvObjectTypes.BackgroundColor = SystemColors.Window;
            dgvObjectTypes.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvObjectTypes.Columns.AddRange(new DataGridViewColumn[] { colUse, colType, colStyle });
            dgvObjectTypes.EditMode = DataGridViewEditMode.EditOnEnter;
            dgvObjectTypes.Location = new Point(24, 74);
            dgvObjectTypes.MultiSelect = false;
            dgvObjectTypes.Name = "dgvObjectTypes";
            dgvObjectTypes.RowHeadersVisible = false;
            dgvObjectTypes.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvObjectTypes.Size = new Size(892, 230);
            dgvObjectTypes.TabIndex = 4;
            dgvObjectTypes.CurrentCellDirtyStateChanged += dgvObjectTypes_CurrentCellDirtyStateChanged;
            dgvObjectTypes.CellValueChanged += dgvObjectTypes_CellValueChanged;
            dgvObjectTypes.SelectionChanged += dgvObjectTypes_SelectionChanged;
            dgvObjectTypes.DataError += dgvObjectTypes_DataError;

            // 
            // colUse
            // 
            colUse.HeaderText = "Use";
            colUse.Name = "colUse";
            colUse.Width = 55;

            // 
            // colType
            // 
            colType.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colType.HeaderText = "Type";
            colType.Name = "colType";
            colType.ReadOnly = true;

            // 
            // colStyle
            // 
            colStyle.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
            colStyle.HeaderText = "Template Style";
            colStyle.Items.AddRange(new object[] { "Molecule / Substance", "Generic / Free-text" });
            colStyle.Name = "colStyle";
            colStyle.Width = 210;

            // 
            // grpExamplePreviews
            // 
            grpExamplePreviews.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grpExamplePreviews.Controls.Add(btnPreviewMolecule);
            grpExamplePreviews.Controls.Add(btnPreviewGeneric);
            grpExamplePreviews.Controls.Add(lblPreviewActiveHint);
            grpExamplePreviews.Controls.Add(lblPreviewSmallBoxTitle);
            grpExamplePreviews.Controls.Add(picPreviewSmallBox);
            grpExamplePreviews.Controls.Add(lblPreviewMagnetTitle);
            grpExamplePreviews.Controls.Add(picPreviewMagnet);
            grpExamplePreviews.Controls.Add(lblPreviewFlaskTitle);
            grpExamplePreviews.Controls.Add(picPreviewFlask);
            grpExamplePreviews.Location = new Point(24, 314);
            grpExamplePreviews.Name = "grpExamplePreviews";
            grpExamplePreviews.Size = new Size(892, 224);
            grpExamplePreviews.TabIndex = 5;
            grpExamplePreviews.TabStop = false;
            grpExamplePreviews.Text = "Live Label Example Previews";

            // 
            // btnPreviewMolecule
            // 
            btnPreviewMolecule.FlatStyle = FlatStyle.Flat;
            btnPreviewMolecule.Location = new Point(16, 24);
            btnPreviewMolecule.Name = "btnPreviewMolecule";
            btnPreviewMolecule.Size = new Size(220, 30);
            btnPreviewMolecule.TabIndex = 0;
            btnPreviewMolecule.Text = "Preview: Molecule / Substance";
            btnPreviewMolecule.UseVisualStyleBackColor = false;
            btnPreviewMolecule.Click += btnPreviewMolecule_Click;

            // 
            // btnPreviewGeneric
            // 
            btnPreviewGeneric.FlatStyle = FlatStyle.Flat;
            btnPreviewGeneric.Location = new Point(246, 24);
            btnPreviewGeneric.Name = "btnPreviewGeneric";
            btnPreviewGeneric.Size = new Size(220, 30);
            btnPreviewGeneric.TabIndex = 1;
            btnPreviewGeneric.Text = "Preview: Generic / Free-text";
            btnPreviewGeneric.UseVisualStyleBackColor = false;
            btnPreviewGeneric.Click += btnPreviewGeneric_Click;

            // 
            // lblPreviewActiveHint
            // 
            lblPreviewActiveHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblPreviewActiveHint.AutoEllipsis = true;
            lblPreviewActiveHint.ForeColor = Color.DimGray;
            lblPreviewActiveHint.Location = new Point(480, 29);
            lblPreviewActiveHint.Name = "lblPreviewActiveHint";
            lblPreviewActiveHint.Size = new Size(396, 20);
            lblPreviewActiveHint.TabIndex = 2;
            lblPreviewActiveHint.Text = "Showing example previews for: Molecule / Substance";

            // 
            // lblPreviewSmallBoxTitle
            // 
            lblPreviewSmallBoxTitle.AutoSize = true;
            lblPreviewSmallBoxTitle.Location = new Point(16, 64);
            lblPreviewSmallBoxTitle.Name = "lblPreviewSmallBoxTitle";
            lblPreviewSmallBoxTitle.Size = new Size(106, 15);
            lblPreviewSmallBoxTitle.TabIndex = 3;
            lblPreviewSmallBoxTitle.Text = "Small Box (12mm)";

            // 
            // picPreviewSmallBox
            // 
            picPreviewSmallBox.BackColor = Color.White;
            picPreviewSmallBox.BorderStyle = BorderStyle.FixedSingle;
            picPreviewSmallBox.Location = new Point(16, 84);
            picPreviewSmallBox.Name = "picPreviewSmallBox";
            picPreviewSmallBox.Size = new Size(274, 124);
            picPreviewSmallBox.SizeMode = PictureBoxSizeMode.Zoom;
            picPreviewSmallBox.TabIndex = 4;
            picPreviewSmallBox.TabStop = false;

            // 
            // lblPreviewMagnetTitle
            // 
            lblPreviewMagnetTitle.AutoSize = true;
            lblPreviewMagnetTitle.Location = new Point(309, 64);
            lblPreviewMagnetTitle.Name = "lblPreviewMagnetTitle";
            lblPreviewMagnetTitle.Size = new Size(92, 15);
            lblPreviewMagnetTitle.TabIndex = 5;
            lblPreviewMagnetTitle.Text = "Magnet (12mm)";

            // 
            // picPreviewMagnet
            // 
            picPreviewMagnet.BackColor = Color.White;
            picPreviewMagnet.BorderStyle = BorderStyle.FixedSingle;
            picPreviewMagnet.Location = new Point(309, 84);
            picPreviewMagnet.Name = "picPreviewMagnet";
            picPreviewMagnet.Size = new Size(274, 124);
            picPreviewMagnet.SizeMode = PictureBoxSizeMode.Zoom;
            picPreviewMagnet.TabIndex = 6;
            picPreviewMagnet.TabStop = false;

            // 
            // lblPreviewFlaskTitle
            // 
            lblPreviewFlaskTitle.AutoSize = true;
            lblPreviewFlaskTitle.Location = new Point(602, 64);
            lblPreviewFlaskTitle.Name = "lblPreviewFlaskTitle";
            lblPreviewFlaskTitle.Size = new Size(76, 15);
            lblPreviewFlaskTitle.TabIndex = 7;
            lblPreviewFlaskTitle.Text = "Flask (24mm)";

            // 
            // picPreviewFlask
            // 
            picPreviewFlask.BackColor = Color.White;
            picPreviewFlask.BorderStyle = BorderStyle.FixedSingle;
            picPreviewFlask.Location = new Point(602, 84);
            picPreviewFlask.Name = "picPreviewFlask";
            picPreviewFlask.Size = new Size(274, 124);
            picPreviewFlask.SizeMode = PictureBoxSizeMode.Zoom;
            picPreviewFlask.TabIndex = 8;
            picPreviewFlask.TabStop = false;

            // 
            // pnlPrinters
            // 
            pnlPrinters.Controls.Add(lblPrintersSectionTitle);
            pnlPrinters.Controls.Add(lblPrintersHint);
            pnlPrinters.Controls.Add(lblNoPrintersWarning);
            pnlPrinters.Controls.Add(lblSmallBoxPrinter);
            pnlPrinters.Controls.Add(cmbSmallBoxPrinter);
            pnlPrinters.Controls.Add(lblMagnetPrinter);
            pnlPrinters.Controls.Add(cmbMagnetPrinter);
            pnlPrinters.Controls.Add(lblFlaskPrinter);
            pnlPrinters.Controls.Add(cmbFlaskPrinter);
            pnlPrinters.Dock = DockStyle.Fill;
            pnlPrinters.Location = new Point(0, 0);
            pnlPrinters.Name = "pnlPrinters";
            pnlPrinters.Size = new Size(940, 552);
            pnlPrinters.TabIndex = 2;
            pnlPrinters.Visible = false;

            // 
            // lblPrintersSectionTitle
            // 
            lblPrintersSectionTitle.AutoSize = true;
            lblPrintersSectionTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            lblPrintersSectionTitle.Location = new Point(36, 28);
            lblPrintersSectionTitle.Name = "lblPrintersSectionTitle";
            lblPrintersSectionTitle.Size = new Size(176, 20);
            lblPrintersSectionTitle.TabIndex = 0;
            lblPrintersSectionTitle.Text = "Label Printer Routing";

            // 
            // lblPrintersHint
            // 
            lblPrintersHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblPrintersHint.ForeColor = Color.DimGray;
            lblPrintersHint.Location = new Point(36, 56);
            lblPrintersHint.Name = "lblPrintersHint";
            lblPrintersHint.Size = new Size(868, 22);
            lblPrintersHint.TabIndex = 1;
            lblPrintersHint.Text = "Assign a Windows printer queue for each label template slot.";

            // 
            // lblNoPrintersWarning
            // 
            lblNoPrintersWarning.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblNoPrintersWarning.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            lblNoPrintersWarning.ForeColor = Color.FromArgb(180, 83, 9);
            lblNoPrintersWarning.Location = new Point(36, 90);
            lblNoPrintersWarning.Name = "lblNoPrintersWarning";
            lblNoPrintersWarning.Size = new Size(868, 28);
            lblNoPrintersWarning.TabIndex = 2;
            lblNoPrintersWarning.Text = "No Windows printers detected — saved/default printer names will be kept.";
            lblNoPrintersWarning.Visible = false;

            // 
            // lblSmallBoxPrinter
            // 
            lblSmallBoxPrinter.AutoSize = true;
            lblSmallBoxPrinter.Location = new Point(36, 134);
            lblSmallBoxPrinter.Name = "lblSmallBoxPrinter";
            lblSmallBoxPrinter.Size = new Size(149, 15);
            lblSmallBoxPrinter.TabIndex = 3;
            lblSmallBoxPrinter.Text = "Small Box (12mm) Printer:";

            // 
            // cmbSmallBoxPrinter
            // 
            cmbSmallBoxPrinter.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbSmallBoxPrinter.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSmallBoxPrinter.FormattingEnabled = true;
            cmbSmallBoxPrinter.Location = new Point(36, 158);
            cmbSmallBoxPrinter.Name = "cmbSmallBoxPrinter";
            cmbSmallBoxPrinter.Size = new Size(868, 28);
            cmbSmallBoxPrinter.TabIndex = 4;
            cmbSmallBoxPrinter.SelectedIndexChanged += PrinterCombo_SelectedIndexChanged;

            // 
            // lblMagnetPrinter
            // 
            lblMagnetPrinter.AutoSize = true;
            lblMagnetPrinter.Location = new Point(36, 206);
            lblMagnetPrinter.Name = "lblMagnetPrinter";
            lblMagnetPrinter.Size = new Size(135, 15);
            lblMagnetPrinter.TabIndex = 5;
            lblMagnetPrinter.Text = "Magnet (12mm) Printer:";

            // 
            // cmbMagnetPrinter
            // 
            cmbMagnetPrinter.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbMagnetPrinter.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMagnetPrinter.FormattingEnabled = true;
            cmbMagnetPrinter.Location = new Point(36, 230);
            cmbMagnetPrinter.Name = "cmbMagnetPrinter";
            cmbMagnetPrinter.Size = new Size(868, 28);
            cmbMagnetPrinter.TabIndex = 6;
            cmbMagnetPrinter.SelectedIndexChanged += PrinterCombo_SelectedIndexChanged;

            // 
            // lblFlaskPrinter
            // 
            lblFlaskPrinter.AutoSize = true;
            lblFlaskPrinter.Location = new Point(36, 278);
            lblFlaskPrinter.Name = "lblFlaskPrinter";
            lblFlaskPrinter.Size = new Size(119, 15);
            lblFlaskPrinter.TabIndex = 7;
            lblFlaskPrinter.Text = "Flask (24mm) Printer:";

            // 
            // cmbFlaskPrinter
            // 
            cmbFlaskPrinter.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbFlaskPrinter.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFlaskPrinter.FormattingEnabled = true;
            cmbFlaskPrinter.Location = new Point(36, 302);
            cmbFlaskPrinter.Name = "cmbFlaskPrinter";
            cmbFlaskPrinter.Size = new Size(868, 28);
            cmbFlaskPrinter.TabIndex = 8;
            cmbFlaskPrinter.SelectedIndexChanged += PrinterCombo_SelectedIndexChanged;

            // 
            // SetupSettingsForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = btnCancel;
            ClientSize = new Size(940, 680);
            Controls.Add(pnlContentHost);
            Controls.Add(pnlBottomBar);
            Controls.Add(pnlTopBar);
            MinimumSize = new Size(956, 719);
            Name = "SetupSettingsForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "openBIS Label Studio Setup";
            pnlTopBar.ResumeLayout(false);
            pnlWizardHeader.ResumeLayout(false);
            pnlWizardHeader.PerformLayout();
            pnlSettingsTabs.ResumeLayout(false);
            pnlBottomBar.ResumeLayout(false);
            pnlContentHost.ResumeLayout(false);
            pnlConnection.ResumeLayout(false);
            pnlConnection.PerformLayout();
            pnlObjectTypes.ResumeLayout(false);
            pnlObjectTypes.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvObjectTypes).EndInit();
            grpExamplePreviews.ResumeLayout(false);
            grpExamplePreviews.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picPreviewSmallBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)picPreviewMagnet).EndInit();
            ((System.ComponentModel.ISupportInitialize)picPreviewFlask).EndInit();
            pnlPrinters.ResumeLayout(false);
            pnlPrinters.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlTopBar = null!;
        private Panel pnlWizardHeader = null!;
        private Label lblWizardTitle = null!;
        private Label lblWizardSubtitle = null!;
        private Panel pnlSettingsTabs = null!;
        private Button btnTabConnection = null!;
        private Button btnTabObjectTypes = null!;
        private Button btnTabPrinters = null!;
        private Panel pnlTopDivider = null!;
        private Panel pnlBottomBar = null!;
        private Panel pnlBottomDivider = null!;
        private Label lblFooterError = null!;
        private Button btnBack = null!;
        private Button btnNextFinish = null!;
        private Button btnCancel = null!;
        private Panel pnlContentHost = null!;
        private Panel pnlConnection = null!;
        private Label lblConnSectionTitle = null!;
        private Label lblConnHint = null!;
        private Label lblInstanceUrl = null!;
        private TextBox txtInstanceUrl = null!;
        private Label lblToken = null!;
        private TextBox txtToken = null!;
        private CheckBox chkShowToken = null!;
        private Button btnTestConnection = null!;
        private ProgressBar prgConnectionLoading = null!;
        private Label lblConnectionStatus = null!;
        private Panel pnlObjectTypes = null!;
        private Label lblTypeSearch = null!;
        private TextBox txtTypeFilter = null!;
        private Button btnRefreshTypes = null!;
        private ProgressBar prgTypesLoading = null!;
        private Label lblTypesStatus = null!;
        private DataGridView dgvObjectTypes = null!;
        private DataGridViewCheckBoxColumn colUse = null!;
        private DataGridViewTextBoxColumn colType = null!;
        private DataGridViewComboBoxColumn colStyle = null!;
        private GroupBox grpExamplePreviews = null!;
        private Button btnPreviewMolecule = null!;
        private Button btnPreviewGeneric = null!;
        private Label lblPreviewActiveHint = null!;
        private Label lblPreviewSmallBoxTitle = null!;
        private PictureBox picPreviewSmallBox = null!;
        private Label lblPreviewMagnetTitle = null!;
        private PictureBox picPreviewMagnet = null!;
        private Label lblPreviewFlaskTitle = null!;
        private PictureBox picPreviewFlask = null!;
        private Panel pnlPrinters = null!;
        private Label lblPrintersSectionTitle = null!;
        private Label lblPrintersHint = null!;
        private Label lblNoPrintersWarning = null!;
        private Label lblSmallBoxPrinter = null!;
        private ComboBox cmbSmallBoxPrinter = null!;
        private Label lblMagnetPrinter = null!;
        private ComboBox cmbMagnetPrinter = null!;
        private Label lblFlaskPrinter = null!;
        private ComboBox cmbFlaskPrinter = null!;
    }
}
