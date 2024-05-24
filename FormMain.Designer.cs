namespace HDF_ShowMap
{
    partial class FormMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            formsPlot1 = new ScottPlot.WinForms.FormsPlot();
            btnOpenFile = new Button();
            ttbFileName = new TextBox();
            lblFileName = new Label();
            SuspendLayout();
            // 
            // formsPlot1
            // 
            formsPlot1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            formsPlot1.DisplayScale = 1F;
            formsPlot1.Location = new Point(12, 56);
            formsPlot1.Name = "formsPlot1";
            formsPlot1.Size = new Size(640, 480);
            formsPlot1.TabIndex = 0;
            // 
            // btnOpenFile
            // 
            btnOpenFile.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnOpenFile.Location = new Point(579, 27);
            btnOpenFile.Name = "btnOpenFile";
            btnOpenFile.Size = new Size(75, 23);
            btnOpenFile.TabIndex = 1;
            btnOpenFile.Text = "Abrir...";
            btnOpenFile.UseVisualStyleBackColor = true;
            btnOpenFile.Click += BtnOpenFile_Click;
            // 
            // ttbFileName
            // 
            ttbFileName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            ttbFileName.Location = new Point(12, 27);
            ttbFileName.Name = "ttbFileName";
            ttbFileName.Size = new Size(561, 23);
            ttbFileName.TabIndex = 2;
            // 
            // lblFileName
            // 
            lblFileName.AutoSize = true;
            lblFileName.Location = new Point(12, 9);
            lblFileName.Name = "lblFileName";
            lblFileName.Size = new Size(52, 15);
            lblFileName.TabIndex = 3;
            lblFileName.Text = "Arquivo:";
            // 
            // FormMain
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(666, 551);
            Controls.Add(lblFileName);
            Controls.Add(ttbFileName);
            Controls.Add(btnOpenFile);
            Controls.Add(formsPlot1);
            MinimumSize = new Size(682, 590);
            Name = "FormMain";
            Text = "FormMain";
            Load += FormMain_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ScottPlot.WinForms.FormsPlot formsPlot1;
        private Button btnOpenFile;
        private TextBox ttbFileName;
        private Label lblFileName;
    }
}