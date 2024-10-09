using HDF.PInvoke;
using HDF5CSharp;
using HDF5CSharp.DataTypes;
using ScottPlot.Plottables;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HDF_ShowMap
{
    public partial class FormMain : Form
    {
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;

        public FormMain()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            InitializeComponent();
        }
        private void ShowDatasets(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException("Arquivo não encontrado.");
            }

            long fileId = H5F.open(fileName, H5F.ACC_RDONLY);
            if (fileId < 0)
            {
                throw new FileLoadException("Falha ao abrir o arquivo.");
            }

            var elements = Hdf5.ReadFlatFileStructure(fileName);
            if (elements.Count > 0)
            {
                dataGridView1.Rows.Clear();
            }

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
        private void ReadHDF(string fileName, string dataSetPath)
        {
            if (!File.Exists(fileName))
            {
                MessageBox.Show("Arquivo inválido ou nulo.",
                    "Falha ao abrir dataset",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            formsPlot1.Plot.Clear();
            formsPlot1.Reset();
            formsPlot1.Refresh();

            _ = H5.open();
            long fileId = H5F.open(fileName, H5F.ACC_RDONLY); // Abrir arquivo para somente leitura
            //string dataSetPath = "/XRF/" + Path.GetFileNameWithoutExtension(fileName);
            long dataSetId = H5D.open(fileId, dataSetPath);
            long dataSpace = H5D.get_space(dataSetId);

            int rank = H5S.get_simple_extent_ndims(dataSpace); // Quantidade de dimensões do dataset
            ulong[] dims = new ulong[rank];
            _ = H5S.get_simple_extent_dims(dataSpace, dims, null);

            // HeatMap data
            double[,] data = new double[dims[1], dims[0]];

            Heatmap hm = formsPlot1.Plot.Add.Heatmap(data);
            hm.FlipVertically = true; // Inverter eixo y
            formsPlot1.Plot.Axes.AutoScale();

            hm.Colormap = new ScottPlot.Colormaps.Viridis(); // mapa de cores
            hm.Smooth = false;
            var cb = formsPlot1.Plot.Add.ColorBar(hm); // colorbar

            int threadCount = Environment.ProcessorCount;
            if (threadCount > 1)
            {
                // Remover um thread para manter a thread da interface desocupada
                threadCount--;
            }

            int rowsPerThread = (int)dims[1] / threadCount;
            int remaingRows = (int)dims[1] - rowsPerThread * threadCount;

            // Definindo o hyperslab
            ulong[] start = [0, 0, 0];
            ulong[] count = [dims[0], dims[1], dims[2]]; // Selecionar quantidade de valores
            _ = H5S.select_hyperslab(dataSpace, H5S.seloper_t.SET, start, null, count, null);

            // Alocar espaço na memória para leitura dos dados
            long memSpace = H5S.create_simple(3, count, null);

            // Ler o dataset do HDF, linha por linha
            double[,,] hdfData = new double[dims[0], dims[1], dims[2]]; // x, y, z
            GCHandle dataHandle = GCHandle.Alloc(hdfData, GCHandleType.Pinned);
            _ = H5D.read(dataSetId, H5T.NATIVE_DOUBLE, memSpace, dataSpace, H5P.DEFAULT, dataHandle.AddrOfPinnedObject());
            dataHandle.Free();
            _ = H5S.close(memSpace);

            Task[] tasks = new Task[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                ulong startRow = (ulong)(rowsPerThread * i);
                ulong rowsCount = (ulong)(i + 1 == threadCount ? rowsPerThread + remaingRows : rowsPerThread);

                // somar valores
                tasks[i] = Task.Run(() =>
                {
                    Debug.WriteLine($"Runnning Task n#: {i}.");
                    for (ulong y = startRow; y < startRow + rowsCount; y++)
                    {
                        if (_cancellationToken.IsCancellationRequested)
                        {
                            Debug.WriteLine($"Task n#: {i} cancelled.");
                            return;
                        }

                        for (ulong x = 0; x < dims[0]; x++)
                        {
                            double sum = 0;

                            for (ulong z = 0; z < dims[2]; z++)
                            {
                                sum += hdfData[x, y, z];
                            }
                            lock (data)
                            {
                                data[y, x] = sum;
                            }
                        }
                    }
                }, _cancellationToken);
                Thread.Sleep(50);
            }
            Task.WaitAll(tasks);

            Invoke(() =>
            {
                hm.Update();
                formsPlot1.Refresh();
            });
        }
        private void BtnOpenFile_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new()
            {
                Filter = "Hierarchical Data Format 5 (*.HDF5; *.H5)|*.hdf5;*.h5"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = new();
                _cancellationToken = _cancellationTokenSource.Token;
                ttbFileName.Text = ofd.FileName;
                ShowDatasets(ttbFileName.Text);
            }
        }
        private void FormMain_Load(object sender, EventArgs e)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;
            formsPlot1.Plot.HideGrid();
            //formsPlot1.Plot.HideLegend();
            //formsPlot1.Plot.Layout.Frameless();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new();
            _cancellationToken = _cancellationTokenSource.Token;

            string? dataset;
            try
            {
                dataset = dataGridView1.SelectedCells[0].FormattedValue.ToString();
                if (dataset == null)
                {
                    return;
                }
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
