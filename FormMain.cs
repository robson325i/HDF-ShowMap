using HDF.PInvoke;
using ScottPlot;
using ScottPlot.Plottables;
using System.Runtime.InteropServices;

namespace HDF_ShowMap
{
    public partial class FormMain : Form
    {
        public FormMain()
        {
            InitializeComponent();
        }

        private void ReadHDF(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return;
            }

            H5.open();
            long fileId = H5F.open(fileName, H5F.ACC_RDONLY); // Abrir arquivo para somente leitura
            string dataSetName = "/" + Path.GetFileNameWithoutExtension(fileName);
            long dataSetId = H5D.open(fileId, dataSetName);
            long dataSpace = H5D.get_space(dataSetId);

            int rank = H5S.get_simple_extent_ndims(dataSpace); // Quantidade de dimensões do dataset
            ulong[] dims = new ulong[rank];
            H5S.get_simple_extent_dims(dataSpace, dims, null);

            // HeatMap data
            double[,] data = new double[dims[0], dims[1]]; // invertido
            Heatmap hm = formsPlot1.Plot.Add.Heatmap(data);
            hm.FlipVertically = true; // Inverter eixo y

            formsPlot1.Plot.Axes.AutoScale();
            hm.Colormap = new ScottPlot.Colormaps.Grayscale(); // mapa de cores
            var cb = formsPlot1.Plot.Add.ColorBar(hm); // colorbar


            // Ler o dataset do HDF, linha por linha
            _ = Task.Run(() =>
            {
                for (ulong y = 0; y < dims[0]; y++)
                {
                    // Definir Hyperslab
                    ulong[] start = [y, 0, 0];
                    ulong[] count = [1, dims[1], dims[2]]; // Selecionar uma linha (valor y)
                    int s = H5S.select_hyperslab(dataSpace, H5S.seloper_t.SET, start, null, count, null);

                    // Alocar espaço na memória para leitura dos dados
                    long memSpace = H5S.create_simple(3, count, null);

                    double[,,] hdfData = new double[1, dims[1], dims[2]]; // y, x, z
                    GCHandle dataHandle = GCHandle.Alloc(hdfData, GCHandleType.Pinned);
                    _ = H5D.read(dataSetId, H5T.NATIVE_DOUBLE, memSpace, dataSpace, H5P.DEFAULT, dataHandle.AddrOfPinnedObject());
                    dataHandle.Free();
                    _ = H5S.close(memSpace);

                    // somar valores
                    for (ulong x = 0; x < dims[1]; x++)
                    {
                        double sum = 0;

                        for (ulong z = 0; z < dims[2]; z++)
                        {
                            sum += hdfData[0, x, z];
                        }

                        data[y, x] = sum;
                    }
                    hm.Update();
                    Invoke(() => formsPlot1.Refresh());
                }
                hm.Smooth = true;

                MessageBox.Show("Carregamento Finalizado");
            });
        }
        private void btnOpenFile_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new()
            {
                Filter = "Hierarchical Data Format 5 (*.HDF5; *.H5)|*.hdf5;*.h5"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                ttbFileName.Text = ofd.FileName;
                ReadHDF(ofd.FileName);
            }
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            formsPlot1.Plot.HideGrid();
            formsPlot1.Plot.HideLegend();
            formsPlot1.Plot.Layout.Frameless();
        }
    }
}
