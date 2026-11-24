namespace OpenBISLabelStudio
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeManagedResources();
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        partial void DisposeManagedResources();

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            openbisTypeLabel = new Label();
            openbisTypeComboBox = new ComboBox();
            selectedOpenbisTypeLabel = new Label();
            openbisObjectsComboBox = new ComboBox();
            sortByLabel = new Label();
            sortByComboBox = new ComboBox();
            btnSortDirection = new Button();
            lblStatusNotice = new Label();
            prgLoading = new ProgressBar();
            loadPrintTemplates = new Button();
            btnPrintLabel = new Button();
            btnSettings = new Button();
            smallBoxLabelGroupBox = new GroupBox();
            smallBoxPrinterComboBox = new ComboBox();
            smallBoxPrinterLabel = new Label();
            smallBoxEvapTempBox = new TextBox();
            smallBoxEvapTempLabel = new Label();
            smallBoxLabelCheckBox = new CheckBox();
            smallBoxLabelPreview = new PictureBox();
            magnetLabelGroupBox = new GroupBox();
            magnetPrinterComboBox = new ComboBox();
            magnetPrinterLabel = new Label();
            magnetEvapTempBox = new TextBox();
            magnetEvapTempLabel = new Label();
            magnetFreeTextBox = new TextBox();
            magnetFreeTextLabel = new Label();
            magnetLabelCheckBox = new CheckBox();
            magnetLabelPreview = new PictureBox();
            flaskLabelGroupBox = new GroupBox();
            flaskPrinterComboBox = new ComboBox();
            flaskPrinterLabel = new Label();
            flaskFreeTextBox = new TextBox();
            flaskFreeTextLabel = new Label();
            flaskLabelCheckBox = new CheckBox();
            flaskLabelPreview = new PictureBox();
            smallBoxLabelGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)smallBoxLabelPreview).BeginInit();
            magnetLabelGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)magnetLabelPreview).BeginInit();
            flaskLabelGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)flaskLabelPreview).BeginInit();
            SuspendLayout();
            // 
            // openbisTypeLabel
            // 
            openbisTypeLabel.AutoSize = true;
            openbisTypeLabel.Location = new Point(40, 25);
            openbisTypeLabel.Name = "openbisTypeLabel";
            openbisTypeLabel.Size = new Size(107, 20);
            openbisTypeLabel.TabIndex = 0;
            openbisTypeLabel.Text = "OpenBIS Type:";
            // 
            // openbisTypeComboBox
            // 
            openbisTypeComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            openbisTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            openbisTypeComboBox.FormattingEnabled = true;
            openbisTypeComboBox.Location = new Point(165, 22);
            openbisTypeComboBox.Name = "openbisTypeComboBox";
            openbisTypeComboBox.Size = new Size(875, 28);
            openbisTypeComboBox.TabIndex = 1;
            openbisTypeComboBox.SelectedIndexChanged += openbisTypeComboBox_SelectedIndexChanged;
            // 
            // selectedOpenbisTypeLabel
            // 
            selectedOpenbisTypeLabel.AutoSize = true;
            selectedOpenbisTypeLabel.Location = new Point(40, 65);
            selectedOpenbisTypeLabel.Name = "selectedOpenbisTypeLabel";
            selectedOpenbisTypeLabel.Size = new Size(43, 20);
            selectedOpenbisTypeLabel.TabIndex = 2;
            selectedOpenbisTypeLabel.Text = "Type:";
            // 
            // openbisObjectsComboBox
            // 
            openbisObjectsComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            openbisObjectsComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            openbisObjectsComboBox.FormattingEnabled = true;
            openbisObjectsComboBox.Location = new Point(165, 62);
            openbisObjectsComboBox.Name = "openbisObjectsComboBox";
            openbisObjectsComboBox.Size = new Size(605, 28);
            openbisObjectsComboBox.TabIndex = 3;
            openbisObjectsComboBox.SelectedIndexChanged += openbisObjectsComboBox_SelectedIndexChanged;
            // 
            // sortByLabel
            // 
            sortByLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sortByLabel.AutoSize = true;
            sortByLabel.Location = new Point(782, 65);
            sortByLabel.Name = "sortByLabel";
            sortByLabel.Size = new Size(42, 20);
            sortByLabel.TabIndex = 4;
            sortByLabel.Text = "Sort:";
            // 
            // sortByComboBox
            // 
            sortByComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sortByComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sortByComboBox.FormattingEnabled = true;
            sortByComboBox.Location = new Point(828, 62);
            sortByComboBox.Name = "sortByComboBox";
            sortByComboBox.Size = new Size(166, 28);
            sortByComboBox.TabIndex = 5;
            sortByComboBox.SelectedIndexChanged += sortByComboBox_SelectedIndexChanged;
            // 
            // btnSortDirection
            // 
            btnSortDirection.AccessibleName = "Sort Direction";
            btnSortDirection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSortDirection.Location = new Point(1002, 62);
            btnSortDirection.Name = "btnSortDirection";
            btnSortDirection.Size = new Size(38, 28);
            btnSortDirection.TabIndex = 6;
            btnSortDirection.Text = "\u2193";
            btnSortDirection.UseVisualStyleBackColor = true;
            btnSortDirection.Click += btnSortDirection_Click;
            // 
            // lblStatusNotice
            // 
            lblStatusNotice.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblStatusNotice.AutoEllipsis = true;
            lblStatusNotice.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblStatusNotice.ForeColor = Color.FromArgb(180, 83, 9);
            lblStatusNotice.Location = new Point(40, 92);
            lblStatusNotice.Name = "lblStatusNotice";
            lblStatusNotice.Size = new Size(810, 20);
            lblStatusNotice.TabIndex = 10;
            lblStatusNotice.Visible = false;
            // 
            // prgLoading
            // 
            prgLoading.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            prgLoading.Location = new Point(860, 95);
            prgLoading.MarqueeAnimationSpeed = 25;
            prgLoading.Name = "prgLoading";
            prgLoading.Size = new Size(180, 14);
            prgLoading.Style = ProgressBarStyle.Marquee;
            prgLoading.TabIndex = 11;
            prgLoading.Visible = false;
            // 
            // loadPrintTemplates
            // 
            loadPrintTemplates.Location = new Point(215, 460);
            loadPrintTemplates.Name = "loadPrintTemplates";
            loadPrintTemplates.Size = new Size(190, 48);
            loadPrintTemplates.TabIndex = 12;
            loadPrintTemplates.Text = "Load Templates";
            loadPrintTemplates.UseVisualStyleBackColor = true;
            loadPrintTemplates.Click += loadPrintTemplates_Click;
            // 
            // btnPrintLabel
            // 
            btnPrintLabel.Location = new Point(445, 460);
            btnPrintLabel.Name = "btnPrintLabel";
            btnPrintLabel.Size = new Size(190, 48);
            btnPrintLabel.TabIndex = 13;
            btnPrintLabel.Text = "Print Labels";
            btnPrintLabel.UseVisualStyleBackColor = true;
            btnPrintLabel.Click += btnPrintLabel_Click_1;
            // 
            // btnSettings
            // 
            btnSettings.Location = new Point(675, 460);
            btnSettings.Name = "btnSettings";
            btnSettings.Size = new Size(190, 48);
            btnSettings.TabIndex = 14;
            btnSettings.Text = "Settings...";
            btnSettings.UseVisualStyleBackColor = true;
            btnSettings.Click += btnSettings_Click;
            // 
            // smallBoxLabelGroupBox
            // 
            smallBoxLabelGroupBox.Controls.Add(smallBoxPrinterComboBox);
            smallBoxLabelGroupBox.Controls.Add(smallBoxPrinterLabel);
            smallBoxLabelGroupBox.Controls.Add(smallBoxEvapTempBox);
            smallBoxLabelGroupBox.Controls.Add(smallBoxEvapTempLabel);
            smallBoxLabelGroupBox.Controls.Add(smallBoxLabelCheckBox);
            smallBoxLabelGroupBox.Controls.Add(smallBoxLabelPreview);
            smallBoxLabelGroupBox.Location = new Point(40, 116);
            smallBoxLabelGroupBox.Name = "smallBoxLabelGroupBox";
            smallBoxLabelGroupBox.Size = new Size(317, 326);
            smallBoxLabelGroupBox.TabIndex = 7;
            smallBoxLabelGroupBox.TabStop = false;
            smallBoxLabelGroupBox.Text = "Small Box 12mm Label";
            smallBoxLabelGroupBox.Enter += groupBox1_Enter;
            // 
            // smallBoxPrinterComboBox
            // 
            smallBoxPrinterComboBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            smallBoxPrinterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            smallBoxPrinterComboBox.FormattingEnabled = true;
            smallBoxPrinterComboBox.Location = new Point(15, 275);
            smallBoxPrinterComboBox.Name = "smallBoxPrinterComboBox";
            smallBoxPrinterComboBox.Size = new Size(287, 28);
            smallBoxPrinterComboBox.TabIndex = 5;
            smallBoxPrinterComboBox.SelectedIndexChanged += PrinterComboBox_SelectedIndexChanged;
            // 
            // smallBoxPrinterLabel
            // 
            smallBoxPrinterLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            smallBoxPrinterLabel.AutoSize = true;
            smallBoxPrinterLabel.Location = new Point(15, 250);
            smallBoxPrinterLabel.Name = "smallBoxPrinterLabel";
            smallBoxPrinterLabel.Size = new Size(55, 20);
            smallBoxPrinterLabel.TabIndex = 4;
            smallBoxPrinterLabel.Text = "Printer:";
            // 
            // smallBoxEvapTempBox
            // 
            smallBoxEvapTempBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            smallBoxEvapTempBox.Location = new Point(15, 195);
            smallBoxEvapTempBox.Name = "smallBoxEvapTempBox";
            smallBoxEvapTempBox.Size = new Size(287, 27);
            smallBoxEvapTempBox.TabIndex = 3;
            smallBoxEvapTempBox.TextChanged += smallBoxEvapTempBox_TextChanged;
            smallBoxEvapTempBox.Leave += smallBoxEvapTempBox_Leave;
            smallBoxEvapTempBox.KeyDown += smallBoxEvapTempBox_KeyDown;
            // 
            // smallBoxEvapTempLabel
            // 
            smallBoxEvapTempLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            smallBoxEvapTempLabel.AutoSize = true;
            smallBoxEvapTempLabel.Location = new Point(15, 170);
            smallBoxEvapTempLabel.Name = "smallBoxEvapTempLabel";
            smallBoxEvapTempLabel.Size = new Size(131, 20);
            smallBoxEvapTempLabel.TabIndex = 2;
            smallBoxEvapTempLabel.Text = "Evaporation Temp:";
            // 
            // smallBoxLabelCheckBox
            // 
            smallBoxLabelCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            smallBoxLabelCheckBox.AutoSize = true;
            smallBoxLabelCheckBox.Location = new Point(287, 15);
            smallBoxLabelCheckBox.Name = "smallBoxLabelCheckBox";
            smallBoxLabelCheckBox.Size = new Size(15, 14);
            smallBoxLabelCheckBox.TabIndex = 1;
            smallBoxLabelCheckBox.UseVisualStyleBackColor = true;
            // 
            // smallBoxLabelPreview
            // 
            smallBoxLabelPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            smallBoxLabelPreview.BorderStyle = BorderStyle.FixedSingle;
            smallBoxLabelPreview.Location = new Point(15, 40);
            smallBoxLabelPreview.Name = "smallBoxLabelPreview";
            smallBoxLabelPreview.Size = new Size(287, 118);
            smallBoxLabelPreview.SizeMode = PictureBoxSizeMode.Zoom;
            smallBoxLabelPreview.TabIndex = 0;
            smallBoxLabelPreview.TabStop = false;
            // 
            // magnetLabelGroupBox
            // 
            magnetLabelGroupBox.Controls.Add(magnetPrinterComboBox);
            magnetLabelGroupBox.Controls.Add(magnetPrinterLabel);
            magnetLabelGroupBox.Controls.Add(magnetEvapTempBox);
            magnetLabelGroupBox.Controls.Add(magnetEvapTempLabel);
            magnetLabelGroupBox.Controls.Add(magnetFreeTextBox);
            magnetLabelGroupBox.Controls.Add(magnetFreeTextLabel);
            magnetLabelGroupBox.Controls.Add(magnetLabelCheckBox);
            magnetLabelGroupBox.Controls.Add(magnetLabelPreview);
            magnetLabelGroupBox.Location = new Point(382, 116);
            magnetLabelGroupBox.Name = "magnetLabelGroupBox";
            magnetLabelGroupBox.Size = new Size(316, 326);
            magnetLabelGroupBox.TabIndex = 8;
            magnetLabelGroupBox.TabStop = false;
            magnetLabelGroupBox.Text = "Magnet 12mm Label";
            // 
            // magnetPrinterComboBox
            // 
            magnetPrinterComboBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            magnetPrinterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            magnetPrinterComboBox.FormattingEnabled = true;
            magnetPrinterComboBox.Location = new Point(15, 275);
            magnetPrinterComboBox.Name = "magnetPrinterComboBox";
            magnetPrinterComboBox.Size = new Size(286, 28);
            magnetPrinterComboBox.TabIndex = 5;
            magnetPrinterComboBox.SelectedIndexChanged += PrinterComboBox_SelectedIndexChanged;
            // 
            // magnetPrinterLabel
            // 
            magnetPrinterLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            magnetPrinterLabel.AutoSize = true;
            magnetPrinterLabel.Location = new Point(15, 250);
            magnetPrinterLabel.Name = "magnetPrinterLabel";
            magnetPrinterLabel.Size = new Size(55, 20);
            magnetPrinterLabel.TabIndex = 4;
            magnetPrinterLabel.Text = "Printer:";
            // 
            // magnetEvapTempBox
            // 
            magnetEvapTempBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            magnetEvapTempBox.Location = new Point(15, 195);
            magnetEvapTempBox.Name = "magnetEvapTempBox";
            magnetEvapTempBox.Size = new Size(286, 27);
            magnetEvapTempBox.TabIndex = 3;
            magnetEvapTempBox.TextChanged += magnetEvapTempBox_TextChanged;
            magnetEvapTempBox.Leave += magnetEvapTempBox_Leave;
            magnetEvapTempBox.KeyDown += magnetEvapTempBox_KeyDown;
            // 
            // magnetEvapTempLabel
            // 
            magnetEvapTempLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            magnetEvapTempLabel.AutoSize = true;
            magnetEvapTempLabel.Location = new Point(15, 170);
            magnetEvapTempLabel.Name = "magnetEvapTempLabel";
            magnetEvapTempLabel.Size = new Size(131, 20);
            magnetEvapTempLabel.TabIndex = 2;
            magnetEvapTempLabel.Text = "Evaporation Temp:";
            // 
            // magnetFreeTextBox
            // 
            magnetFreeTextBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            magnetFreeTextBox.Location = new Point(15, 195);
            magnetFreeTextBox.Name = "magnetFreeTextBox";
            magnetFreeTextBox.Size = new Size(286, 27);
            magnetFreeTextBox.TabIndex = 3;
            magnetFreeTextBox.Leave += magnetFreeTextBox_Leave;
            magnetFreeTextBox.KeyDown += magnetFreeTextBox_KeyDown;
            // 
            // magnetFreeTextLabel
            // 
            magnetFreeTextLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            magnetFreeTextLabel.AutoSize = true;
            magnetFreeTextLabel.Location = new Point(15, 170);
            magnetFreeTextLabel.Name = "magnetFreeTextLabel";
            magnetFreeTextLabel.Size = new Size(71, 20);
            magnetFreeTextLabel.TabIndex = 2;
            magnetFreeTextLabel.Text = "Free Text:";
            // 
            // magnetLabelCheckBox
            // 
            magnetLabelCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            magnetLabelCheckBox.AutoSize = true;
            magnetLabelCheckBox.Location = new Point(286, 15);
            magnetLabelCheckBox.Name = "magnetLabelCheckBox";
            magnetLabelCheckBox.Size = new Size(15, 14);
            magnetLabelCheckBox.TabIndex = 1;
            magnetLabelCheckBox.UseVisualStyleBackColor = true;
            // 
            // magnetLabelPreview
            // 
            magnetLabelPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            magnetLabelPreview.BorderStyle = BorderStyle.FixedSingle;
            magnetLabelPreview.Location = new Point(15, 40);
            magnetLabelPreview.Name = "magnetLabelPreview";
            magnetLabelPreview.Size = new Size(286, 118);
            magnetLabelPreview.SizeMode = PictureBoxSizeMode.Zoom;
            magnetLabelPreview.TabIndex = 0;
            magnetLabelPreview.TabStop = false;
            // 
            // flaskLabelGroupBox
            // 
            flaskLabelGroupBox.Controls.Add(flaskPrinterComboBox);
            flaskLabelGroupBox.Controls.Add(flaskPrinterLabel);
            flaskLabelGroupBox.Controls.Add(flaskFreeTextBox);
            flaskLabelGroupBox.Controls.Add(flaskFreeTextLabel);
            flaskLabelGroupBox.Controls.Add(flaskLabelCheckBox);
            flaskLabelGroupBox.Controls.Add(flaskLabelPreview);
            flaskLabelGroupBox.Location = new Point(723, 116);
            flaskLabelGroupBox.Name = "flaskLabelGroupBox";
            flaskLabelGroupBox.Size = new Size(317, 326);
            flaskLabelGroupBox.TabIndex = 9;
            flaskLabelGroupBox.TabStop = false;
            flaskLabelGroupBox.Text = "Flask 24mm Label";
            // 
            // flaskPrinterComboBox
            // 
            flaskPrinterComboBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            flaskPrinterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            flaskPrinterComboBox.FormattingEnabled = true;
            flaskPrinterComboBox.Location = new Point(15, 275);
            flaskPrinterComboBox.Name = "flaskPrinterComboBox";
            flaskPrinterComboBox.Size = new Size(287, 28);
            flaskPrinterComboBox.TabIndex = 5;
            flaskPrinterComboBox.SelectedIndexChanged += PrinterComboBox_SelectedIndexChanged;
            // 
            // flaskPrinterLabel
            // 
            flaskPrinterLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            flaskPrinterLabel.AutoSize = true;
            flaskPrinterLabel.Location = new Point(15, 250);
            flaskPrinterLabel.Name = "flaskPrinterLabel";
            flaskPrinterLabel.Size = new Size(55, 20);
            flaskPrinterLabel.TabIndex = 4;
            flaskPrinterLabel.Text = "Printer:";
            // 
            // flaskFreeTextBox
            // 
            flaskFreeTextBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            flaskFreeTextBox.Location = new Point(15, 195);
            flaskFreeTextBox.Name = "flaskFreeTextBox";
            flaskFreeTextBox.Size = new Size(287, 27);
            flaskFreeTextBox.TabIndex = 3;
            flaskFreeTextBox.Leave += flaskFreeTextBox_Leave;
            flaskFreeTextBox.KeyDown += flaskFreeTextBox_KeyDown;
            // 
            // flaskFreeTextLabel
            // 
            flaskFreeTextLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            flaskFreeTextLabel.AutoSize = true;
            flaskFreeTextLabel.Location = new Point(15, 170);
            flaskFreeTextLabel.Name = "flaskFreeTextLabel";
            flaskFreeTextLabel.Size = new Size(71, 20);
            flaskFreeTextLabel.TabIndex = 2;
            flaskFreeTextLabel.Text = "Free Text:";
            // 
            // flaskLabelCheckBox
            // 
            flaskLabelCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            flaskLabelCheckBox.AutoSize = true;
            flaskLabelCheckBox.Location = new Point(287, 15);
            flaskLabelCheckBox.Name = "flaskLabelCheckBox";
            flaskLabelCheckBox.Size = new Size(15, 14);
            flaskLabelCheckBox.TabIndex = 1;
            flaskLabelCheckBox.UseVisualStyleBackColor = true;
            // 
            // flaskLabelPreview
            // 
            flaskLabelPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            flaskLabelPreview.BorderStyle = BorderStyle.FixedSingle;
            flaskLabelPreview.Location = new Point(15, 40);
            flaskLabelPreview.Name = "flaskLabelPreview";
            flaskLabelPreview.Size = new Size(287, 118);
            flaskLabelPreview.SizeMode = PictureBoxSizeMode.Zoom;
            flaskLabelPreview.TabIndex = 0;
            flaskLabelPreview.TabStop = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1080, 530);
            Controls.Add(flaskLabelGroupBox);
            Controls.Add(magnetLabelGroupBox);
            Controls.Add(smallBoxLabelGroupBox);
            Controls.Add(btnSettings);
            Controls.Add(btnPrintLabel);
            Controls.Add(loadPrintTemplates);
            Controls.Add(prgLoading);
            Controls.Add(lblStatusNotice);
            Controls.Add(btnSortDirection);
            Controls.Add(sortByComboBox);
            Controls.Add(sortByLabel);
            Controls.Add(openbisObjectsComboBox);
            Controls.Add(selectedOpenbisTypeLabel);
            Controls.Add(openbisTypeComboBox);
            Controls.Add(openbisTypeLabel);
            Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "Form1";
            Text = "openBIS Label Studio";
            Load += Form1_Load;
            smallBoxLabelGroupBox.ResumeLayout(false);
            smallBoxLabelGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)smallBoxLabelPreview).EndInit();
            magnetLabelGroupBox.ResumeLayout(false);
            magnetLabelGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)magnetLabelPreview).EndInit();
            flaskLabelGroupBox.ResumeLayout(false);
            flaskLabelGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)flaskLabelPreview).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label openbisTypeLabel;
        private ComboBox openbisTypeComboBox;
        private Label selectedOpenbisTypeLabel;
        private ComboBox openbisObjectsComboBox;
        private Label sortByLabel;
        private ComboBox sortByComboBox;
        private Button btnSortDirection;
        private Label lblStatusNotice;
        private ProgressBar prgLoading;
        private Button loadPrintTemplates;
        private Button btnPrintLabel;
        private Button btnSettings;
        private GroupBox smallBoxLabelGroupBox;
        private PictureBox smallBoxLabelPreview;
        private CheckBox smallBoxLabelCheckBox;
        private Label smallBoxPrinterLabel;
        private ComboBox smallBoxPrinterComboBox;
        private Label smallBoxEvapTempLabel;
        private TextBox smallBoxEvapTempBox;
        private GroupBox magnetLabelGroupBox;
        private PictureBox magnetLabelPreview;
        private CheckBox magnetLabelCheckBox;
        private Label magnetEvapTempLabel;
        private TextBox magnetEvapTempBox;
        private Label magnetFreeTextLabel;
        private TextBox magnetFreeTextBox;
        private Label magnetPrinterLabel;
        private ComboBox magnetPrinterComboBox;
        private GroupBox flaskLabelGroupBox;
        private PictureBox flaskLabelPreview;
        private CheckBox flaskLabelCheckBox;
        private Label flaskFreeTextLabel;
        private TextBox flaskFreeTextBox;
        private Label flaskPrinterLabel;
        private ComboBox flaskPrinterComboBox;
    }
}
