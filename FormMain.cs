using HDF.PInvoke;
using HDF5CSharp;
using HDF5CSharp.DataTypes;
using ScottPlot.Plottables;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HDF_ShowMap
{
    public partial class FormMain : Form
    {
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;

        private Heatmap _hm1;
        private Heatmap _hm2;

        // Cubos 3D com os 2048 canais espectrais [X, Y, Z]
        private double[,,] _data1_3D;
        private double[,,] _data2_3D;

        // Projeções 2D para exibição no Heatmap
        private double[,] _data1_2D;
        private double[,] _data2_2D;

        private Button btnOpenSecondFile;
        private TextBox ttbSecondFileName;
        private Label lblOffsetX;
        private NumericUpDown numOffsetX;
        private Label lblOffsetY;
        private NumericUpDown numOffsetY;
        private Button btnExportPng;
        private Button btnExportHdf5;

        public FormMain()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            InitializeComponent();
            InitializeStitchControls();
        }

        private void InitializeStitchControls()
        {
            dataGridView1.Height = 110;

            int leftMargin = dataGridView1.Left;
            int width = dataGridView1.Width;
            int startY = dataGridView1.Bottom + 6;

            btnOpenSecondFile = new Button
            {
                Text = "Carregar 2º Arquivo",
                Location = new Point(leftMargin, startY),
                Size = new Size(width, 26)
            };
            btnOpenSecondFile.Click += BtnOpenSecondFile_Click;

            ttbSecondFileName = new TextBox
            {
                Location = new Point(leftMargin, startY + 28),
                Size = new Size(width, 23),
                ReadOnly = true
            };

            lblOffsetX = new Label
            {
                Text = "Offset X:",
                Location = new Point(leftMargin, startY + 56),
                AutoSize = true
            };
            numOffsetX = new NumericUpDown
            {
                Location = new Point(leftMargin + 60, startY + 54),
                Size = new Size(width - 60, 23),
                Minimum = -2000,
                Maximum = 2000,
                Value = 0
            };

            lblOffsetY = new Label
            {
                Text = "Offset Y:",
                Location = new Point(leftMargin, startY + 82),
                AutoSize = true
            };
            numOffsetY = new NumericUpDown
            {
                Location = new Point(leftMargin + 60, startY + 80),
                Size = new Size(width - 60, 23),
                Minimum = -2000,
                Maximum = 2000,
                Value = 0
            };

            btnExportPng = new Button
            {
                Text = "Exportar PNG",
                Location = new Point(leftMargin, startY + 110),
                Size = new Size((width / 2) - 2, 26)
            };
            btnExportPng.Click += BtnExportPng_Click;

            btnExportHdf5 = new Button
            {
                Text = "Exportar HDF5 3D",
                Location = new Point(leftMargin + (width / 2) + 2, startY + 110),
                Size = new Size((width / 2) - 2, 26)
            };
            btnExportHdf5.Click += BtnExportHdf5_Click;

            numOffsetX.ValueChanged += UpdateSecondMapPosition;
            numOffsetY.ValueChanged += UpdateSecondMapPosition;

            Control parent = dataGridView1.Parent ?? this;
            parent.Controls.Add(btnOpenSecondFile);
            parent.Controls.Add(ttbSecondFileName);
            parent.Controls.Add(lblOffsetX);
            parent.Controls.Add(numOffsetX);
            parent.Controls.Add(lblOffsetY);
            parent.Controls.Add(numOffsetY);
            parent.Controls.Add(btnExportPng);
            parent.Controls.Add(btnExportHdf5);

            btnOpenSecondFile.BringToFront();
            ttbSecondFileName.BringToFront();
            numOffsetX.BringToFront();
            numOffsetY.BringToFront();
            btnExportPng.BringToFront();
            btnExportHdf5.BringToFront();
        }

        private void ShowDatasets(string fileName)
        {
            if (!File.Exists(fileName)) return;

            dataGridView1.Rows.Clear();

            try
            {
                var elements = Hdf5.ReadFlatFileStructure(fileName);
                if (elements != null && elements.Count > 0)
                {
                    foreach (Hdf5Element element in elements)
                    {
                        if (element.Type == Hdf5ElementType.Dataset)
                        {
                            int i = dataGridView1.Rows.Add(element.Name);
                            dataGridView1.Rows[i].Resizable = DataGridViewTriState.False;
                            dataGridView1.Rows[i].HeaderCell = null;
                        }
                    }
                }
            }
            catch
            {
                // Se a leitura da estrutura falhar, ignora o erro
            }

            // Se o grid continuar vazio (ex: arquivo recém-gerado ou estrutura customizada),
            // força a adição do caminho /XRF/Spectra se o dataset existir no HDF5
            if (dataGridView1.Rows.Count == 0)
            {
                long fileId = H5F.open(fileName, H5F.ACC_RDONLY);
                if (fileId >= 0)
                {
                    long dataSetId = H5D.open(fileId, "/XRF/Spectra");
                    if (dataSetId >= 0)
                    {
                        int i = dataGridView1.Rows.Add("/XRF/Spectra");
                        dataGridView1.Rows[i].Resizable = DataGridViewTriState.False;
                        dataGridView1.Rows[i].HeaderCell = null;
                        H5D.close(dataSetId);
                    }
                    H5F.close(fileId);
                }
            }
        }

        private (double[,,] cube3D, double[,] map2D) LoadHdfData(string fileName, string dataSetPath)
        {
            if (!File.Exists(fileName)) return (null, null);

            H5.open();
            long fileId = H5F.open(fileName, H5F.ACC_RDONLY);
            if (fileId < 0) return (null, null);

            long dataSetId = H5D.open(fileId, dataSetPath);
            if (dataSetId < 0)
            {
                H5F.close(fileId);
                return (null, null);
            }

            long dataSpace = H5D.get_space(dataSetId);
            int rank = H5S.get_simple_extent_ndims(dataSpace);
            ulong[] dims = new ulong[rank];
            H5S.get_simple_extent_dims(dataSpace, dims, null);

            // Cubo 3D (XRF com 2048 canais espectrais)
            if (rank == 3)
            {
                ulong[] start = new ulong[] { 0, 0, 0 };
                ulong[] count = new ulong[] { dims[0], dims[1], dims[2] };
                H5S.select_hyperslab(dataSpace, H5S.seloper_t.SET, start, null, count, null);

                long memSpace = H5S.create_simple(3, count, null);

                double[,,] cube3D = new double[dims[0], dims[1], dims[2]];
                GCHandle dataHandle = GCHandle.Alloc(cube3D, GCHandleType.Pinned);
                H5D.read(dataSetId, H5T.NATIVE_DOUBLE, memSpace, dataSpace, H5P.DEFAULT, dataHandle.AddrOfPinnedObject());
                dataHandle.Free();

                H5S.close(memSpace);
                H5S.close(dataSpace);
                H5D.close(dataSetId);
                H5F.close(fileId);

                int dimX = (int)dims[0];
                int dimY = (int)dims[1];
                int dimZ = (int)dims[2];

                double[,] map2D = new double[dimY, dimX];

                Parallel.For(0, dimY, y =>
                {
                    for (int x = 0; x < dimX; x++)
                    {
                        double sum = 0;
                        for (int z = 0; z < dimZ; z++)
                        {
                            sum += cube3D[x, y, z];
                        }
                        map2D[y, x] = sum;
                    }
                });

                return (cube3D, map2D);
            }
            // Matriz 2D
            else if (rank == 2)
            {
                int height = (int)dims[0];
                int width = (int)dims[1];

                double[,] map2D = new double[height, width];
                GCHandle dataHandle = GCHandle.Alloc(map2D, GCHandleType.Pinned);
                H5D.read(dataSetId, H5T.NATIVE_DOUBLE, H5S.ALL, H5S.ALL, H5P.DEFAULT, dataHandle.AddrOfPinnedObject());
                dataHandle.Free();

                H5S.close(dataSpace);
                H5D.close(dataSetId);
                H5F.close(fileId);

                return (null, map2D);
            }

            H5S.close(dataSpace);
            H5D.close(dataSetId);
            H5F.close(fileId);

            return (null, null);
        }

        private void ReadHDF(string fileName, string dataSetPath)
        {
            if (!File.Exists(fileName)) return;

            // Garante que se o dataSetPath não começar com '/', ele adicione para o HDF5 localizar corretamente
            if (!dataSetPath.StartsWith("/"))
            {
                dataSetPath = "/" + dataSetPath;
            }

            var res1 = LoadHdfData(fileName, dataSetPath);
            _data1_3D = res1.cube3D;
            _data1_2D = res1.map2D;

            if (!string.IsNullOrEmpty(ttbSecondFileName.Text) && File.Exists(ttbSecondFileName.Text))
            {
                var res2 = LoadHdfData(ttbSecondFileName.Text, dataSetPath);
                _data2_3D = res2.cube3D;
                _data2_2D = res2.map2D;
            }
            else
            {
                _data2_3D = null;
                _data2_2D = null;
            }

            PlotMaps();
        }
        private void PlotMaps()
        {
            formsPlot1.Plot.Clear();
            _hm1 = null;
            _hm2 = null;

            if (_data1_2D == null) return;

            _hm1 = formsPlot1.Plot.Add.Heatmap(_data1_2D);
            _hm1.FlipVertically = true;
            _hm1.Colormap = new ScottPlot.Colormaps.Viridis();
            _hm1.Smooth = false;

            if (_data2_2D != null)
            {
                _hm2 = formsPlot1.Plot.Add.Heatmap(_data2_2D);
                _hm2.FlipVertically = true;
                _hm2.Colormap = new ScottPlot.Colormaps.Viridis();
                _hm2.Smooth = false;

                double offX = (double)numOffsetX.Value;
                double offY = (double)numOffsetY.Value;

                int rows2 = _data2_2D.GetLength(0);
                int cols2 = _data2_2D.GetLength(1);

                _hm2.Position = new ScottPlot.CoordinateRect(offX, offX + cols2, offY, offY + rows2);
            }

            formsPlot1.Plot.Axes.SquareUnits();
            formsPlot1.Plot.Add.ColorBar(_hm1);
            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Refresh();
        }

        private void UpdateSecondMapPosition(object sender, EventArgs e)
        {
            if (_hm2 == null || _data2_2D == null) return;

            double offX = (double)numOffsetX.Value;
            double offY = (double)numOffsetY.Value;

            int rows2 = _data2_2D.GetLength(0);
            int cols2 = _data2_2D.GetLength(1);

            _hm2.Position = new ScottPlot.CoordinateRect(offX, offX + cols2, offY, offY + rows2);

            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Refresh();
        }

        private double[,,] GenerateStitchedCube3D()
        {
            if (_data1_3D == null) return null;
            if (_data2_3D == null) return _data1_3D;

            int dimXa = _data1_3D.GetLength(0);
            int dimYa = _data1_3D.GetLength(1);
            int dimZa = _data1_3D.GetLength(2);

            int dimXb = _data2_3D.GetLength(0);
            int dimYb = _data2_3D.GetLength(1);

            int offXb = (int)numOffsetX.Value;
            int offYb = (int)numOffsetY.Value;

            int minX = Math.Min(0, offXb);
            int maxX = Math.Max(dimXa, offXb + dimXb);
            int minY = Math.Min(0, offYb);
            int maxY = Math.Max(dimYa, offYb + dimYb);

            int dimXc = maxX - minX;
            int dimYc = maxY - minY;
            int dimZc = dimZa;

            double[,,] C = new double[dimXc, dimYc, dimZc];

            int startXa = -minX;
            int startYa = -minY;

            int startXb = offXb - minX;
            int startYb = offYb - minY;

            Parallel.For(0, dimXa, x =>
            {
                for (int y = 0; y < dimYa; y++)
                {
                    for (int z = 0; z < dimZc; z++)
                    {
                        C[startXa + x, startYa + y, z] = _data1_3D[x, y, z];
                    }
                }
            });

            Parallel.For(0, dimXb, x =>
            {
                for (int y = 0; y < dimYb; y++)
                {
                    int targetX = startXb + x;
                    int targetY = startYb + y;

                    if (targetX >= 0 && targetX < dimXc && targetY >= 0 && targetY < dimYc)
                    {
                        for (int z = 0; z < dimZc; z++)
                        {
                            double valA = C[targetX, targetY, z];
                            double valB = _data2_3D[x, y, z];

                            C[targetX, targetY, z] = valA > 0 ? (valA + valB) / 2.0 : valB;
                        }
                    }
                }
            });

            return C;
        }

        private void BtnExportHdf5_Click(object sender, EventArgs e)
        {
            double[,,] combinedCube3D = GenerateStitchedCube3D();
            if (combinedCube3D == null)
            {
                MessageBox.Show("Nenhum cubo 3D carregado para exportar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Arquivo HDF5 (*.h5; *.hdf5)|*.h5;*.hdf5",
                FileName = "Mosaico_Stitched_3D.h5"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    if (File.Exists(sfd.FileName))
                    {
                        File.Delete(sfd.FileName);
                    }

                    // Criar o arquivo HDF5
                    long fileId = H5F.create(sfd.FileName, H5F.ACC_TRUNC, H5P.DEFAULT, H5P.DEFAULT);

                    if (fileId >= 0)
                    {
                        // Criar o grupo /XRF
                        long groupId = H5G.create(fileId, "XRF", H5P.DEFAULT, H5P.DEFAULT, H5P.DEFAULT);

                        // Dimensões do cubo 3D [X, Y, Z]
                        ulong[] dims = new ulong[]
                        {
                    (ulong)combinedCube3D.GetLength(0),
                    (ulong)combinedCube3D.GetLength(1),
                    (ulong)combinedCube3D.GetLength(2)
                        };

                        long spaceId = H5S.create_simple(3, dims, null);

                        // Criar o dataset Spectra dentro do grupo /XRF
                        long datasetId = H5D.create(groupId, "Spectra", H5T.NATIVE_DOUBLE, spaceId, H5P.DEFAULT, H5P.DEFAULT, H5P.DEFAULT);

                        // Fixar a memória e gravar os dados
                        GCHandle handle = GCHandle.Alloc(combinedCube3D, GCHandleType.Pinned);
                        H5D.write(datasetId, H5T.NATIVE_DOUBLE, H5S.ALL, H5S.ALL, H5P.DEFAULT, handle.AddrOfPinnedObject());
                        handle.Free();

                        // Fechar todos os recursos HDF5
                        H5D.close(datasetId);
                        H5S.close(spaceId);
                        if (groupId >= 0) H5G.close(groupId);
                        H5F.close(fileId);

                        MessageBox.Show("Cubo 3D HDF5 exportado com sucesso no formato padrão (/XRF/Spectra)!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Não foi possível criar o arquivo HDF5.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erro ao exportar HDF5: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExportPng_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Imagem PNG (*.png)|*.png",
                FileName = "Mosaico_XRF.png"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                formsPlot1.Plot.SavePng(sfd.FileName, 1920, 1080);
                MessageBox.Show("Mosaico PNG exportado com sucesso!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnOpenFile_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Hierarchical Data Format 5 (*.HDF5; *.H5)|*.hdf5;*.h5"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();
                _cancellationToken = _cancellationTokenSource.Token;
                ttbFileName.Text = ofd.FileName;
                ShowDatasets(ttbFileName.Text);
            }
        }

        private void BtnOpenSecondFile_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Hierarchical Data Format 5 (*.HDF5; *.H5)|*.hdf5;*.h5"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                ttbSecondFileName.Text = ofd.FileName;

                if (dataGridView1.SelectedCells.Count > 0 && dataGridView1.SelectedCells[0].Value != null)
                {
                    string dataset = dataGridView1.SelectedCells[0].Value.ToString();
                    ReadHDF(ttbFileName.Text, dataset);
                }
            }
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;
            formsPlot1.Plot.HideGrid();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;

            string dataset;
            try
            {
                dataset = dataGridView1.SelectedCells[0].FormattedValue.ToString();
                if (dataset == null) return;
            }
            catch
            {
                return;
            }

            button3.Enabled = false;
            ReadHDF(ttbFileName.Text, dataset);
            button3.Enabled = true;
        }
    }
}