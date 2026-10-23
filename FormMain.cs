using HDF.PInvoke;
using HDF5CSharp;
using HDF5CSharp.DataTypes;
using ScottPlot.Plottables;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;

namespace HDF_ShowMap
{
    public partial class FormMain : Form
    {
        private Heatmap _hm1;
        private Heatmap _hm2;

        // Cubos espectrais no formato [X, Y, canais espectrais].
        private double[,,] _data1_3D;
        private double[,,] _data2_3D;

        // Projeções 2D usadas apenas para desenhar os heatmaps.
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
                Size = new Size(Math.Max(40, width - 60), 23),
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
                Size = new Size(Math.Max(40, width - 60), 23),
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

        private void AddDatasetRow(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string normalized = path.StartsWith("/") ? path : "/" + path;

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.Cells.Count > 0 &&
                    string.Equals(Convert.ToString(row.Cells[0].Value),
                                  normalized, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            int i = dataGridView1.Rows.Add(normalized);
            dataGridView1.Rows[i].Resizable = DataGridViewTriState.False;
            dataGridView1.Rows[i].HeaderCell = null;
        }

        private void ShowDatasets(string fileName)
        {
            if (!File.Exists(fileName))
                return;

            dataGridView1.Rows.Clear();

            try
            {
                var elements = Hdf5.ReadFlatFileStructure(fileName);
                if (elements != null)
                {
                    foreach (Hdf5Element element in elements)
                    {
                        if (element.Type == Hdf5ElementType.Dataset)
                            AddDatasetRow(element.Name);
                    }
                }
            }
            catch
            {
                // A verificação direta abaixo ainda tenta localizar o dataset padrão.
            }

            // Garante que os nomes usados pelas versões atual e anterior
            // da exportação apareçam na lista, mesmo se a listagem automática
            // da biblioteca não retornar esses caminhos.
            H5.open();
            long fileId = -1;

            string[] knownDatasetPaths =
            {
                "/XRF/Spectra",
                "/XRF/Spectra_Stitched"
            };

            try
            {
                fileId = H5F.open(fileName, H5F.ACC_RDONLY);
                if (fileId >= 0)
                {
                    foreach (string datasetPath in knownDatasetPaths)
                    {
                        long datasetId = -1;

                        try
                        {
                            datasetId = H5D.open(fileId, datasetPath);
                            if (datasetId >= 0)
                                AddDatasetRow(datasetPath);
                        }
                        catch
                        {
                            // Esse caminho pode não existir neste arquivo.
                        }
                        finally
                        {
                            if (datasetId >= 0)
                                H5D.close(datasetId);
                        }
                    }
                }
            }
            catch
            {
                // Se a abertura direta falhar, mantém os datasets
                // que a listagem automática já conseguiu localizar.
            }
            finally
            {
                if (fileId >= 0)
                    H5F.close(fileId);
            }
        }

        private (double[,,] cube3D, double[,] map2D) LoadHdfData(
            string fileName, string dataSetPath)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException("Arquivo HDF5 não encontrado.", fileName);

            string path = dataSetPath.StartsWith("/")
                ? dataSetPath
                : "/" + dataSetPath;

            H5.open();

            long fileId = -1;
            long datasetId = -1;
            long dataSpace = -1;
            long memSpace = -1;
            GCHandle handle = default(GCHandle);

            try
            {
                fileId = H5F.open(fileName, H5F.ACC_RDONLY);
                if (fileId < 0)
                    throw new InvalidOperationException("Não foi possível abrir o arquivo HDF5.");

                datasetId = H5D.open(fileId, path);
                if (datasetId < 0)
                    throw new InvalidOperationException("Dataset não encontrado: " + path);

                dataSpace = H5D.get_space(datasetId);
                int rank = H5S.get_simple_extent_ndims(dataSpace);

                if (rank != 2 && rank != 3)
                    throw new InvalidOperationException(
                        $"O dataset {path} possui {rank} dimensões. São aceitos datasets 2D ou 3D.");

                ulong[] dims = new ulong[rank];
                H5S.get_simple_extent_dims(dataSpace, dims, null);

                for (int i = 0; i < dims.Length; i++)
                {
                    if (dims[i] == 0 || dims[i] > int.MaxValue)
                        throw new InvalidOperationException("Dimensões do dataset inválidas ou grandes demais.");
                }

                if (rank == 3)
                {
                    int dimX = (int)dims[0];
                    int dimY = (int)dims[1];
                    int dimZ = (int)dims[2];

                    double[,,] cube = new double[dimX, dimY, dimZ];

                    ulong[] start = { 0, 0, 0 };
                    ulong[] count = { dims[0], dims[1], dims[2] };

                    if (H5S.select_hyperslab(dataSpace, H5S.seloper_t.SET,
                        start, null, count, null) < 0)
                        throw new InvalidOperationException("Não foi possível selecionar os dados 3D.");

                    memSpace = H5S.create_simple(3, count, null);
                    if (memSpace < 0)
                        throw new InvalidOperationException("Não foi possível criar o espaço de memória 3D.");

                    handle = GCHandle.Alloc(cube, GCHandleType.Pinned);
                    int readStatus = H5D.read(datasetId, H5T.NATIVE_DOUBLE,
                        memSpace, dataSpace, H5P.DEFAULT, handle.AddrOfPinnedObject());

                    if (readStatus < 0)
                        throw new InvalidOperationException("Falha ao ler os valores do dataset 3D.");

                    handle.Free();

                    // Projeção de soma para visualização; o cubo original é preservado.
                    double[,] map = new double[dimY, dimX];
                    for (int y = 0; y < dimY; y++)
                    {
                        for (int x = 0; x < dimX; x++)
                        {
                            double sum = 0;
                            for (int z = 0; z < dimZ; z++)
                                sum += cube[x, y, z];

                            map[y, x] = sum;
                        }
                    }

                    return (cube, map);
                }
                else
                {
                    int height = (int)dims[0];
                    int width = (int)dims[1];
                    double[,] map = new double[height, width];

                    handle = GCHandle.Alloc(map, GCHandleType.Pinned);
                    int readStatus = H5D.read(datasetId, H5T.NATIVE_DOUBLE,
                        H5S.ALL, H5S.ALL, H5P.DEFAULT, handle.AddrOfPinnedObject());

                    if (readStatus < 0)
                        throw new InvalidOperationException("Falha ao ler os valores do dataset 2D.");

                    handle.Free();
                    return (null, map);
                }
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();

                if (memSpace >= 0) H5S.close(memSpace);
                if (dataSpace >= 0) H5S.close(dataSpace);
                if (datasetId >= 0) H5D.close(datasetId);
                if (fileId >= 0) H5F.close(fileId);
            }
        }

        private void ReadHDF(string fileName, string dataSetPath)
        {
            if (!File.Exists(fileName))
                return;

            try
            {
                var first = LoadHdfData(fileName, dataSetPath);
                _data1_3D = first.cube3D;
                _data1_2D = first.map2D;

                if (!string.IsNullOrWhiteSpace(ttbSecondFileName.Text) &&
                    File.Exists(ttbSecondFileName.Text))
                {
                    var second = LoadHdfData(ttbSecondFileName.Text, dataSetPath);
                    _data2_3D = second.cube3D;
                    _data2_2D = second.map2D;
                }
                else
                {
                    _data2_3D = null;
                    _data2_2D = null;
                }

                PlotMaps();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao ler o dataset HDF5:\n" + ex.Message,
                    "Erro de leitura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PlotMaps()
        {
            formsPlot1.Plot.Clear();
            _hm1 = null;
            _hm2 = null;

            if (_data1_2D == null)
            {
                formsPlot1.Refresh();
                return;
            }

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

                _hm2.Position = new ScottPlot.CoordinateRect(
                    offX, offX + cols2, offY, offY + rows2);
            }

            formsPlot1.Plot.Axes.SquareUnits();
            formsPlot1.Plot.Add.ColorBar(_hm1);
            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Refresh();
        }

        private void UpdateSecondMapPosition(object sender, EventArgs e)
        {
            if (_hm2 == null || _data2_2D == null)
                return;

            double offX = (double)numOffsetX.Value;
            double offY = (double)numOffsetY.Value;

            int rows2 = _data2_2D.GetLength(0);
            int cols2 = _data2_2D.GetLength(1);

            _hm2.Position = new ScottPlot.CoordinateRect(
                offX, offX + cols2, offY, offY + rows2);

            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Refresh();
        }



        private double[,,] GenerateStitchedCube3D()
        {
            if (_data1_3D == null)
                return null;

            if (_data2_3D == null)
                return _data1_3D;

            int dimXa = _data1_3D.GetLength(0);
            int dimYa = _data1_3D.GetLength(1);
            int dimZa = _data1_3D.GetLength(2);

            int dimXb = _data2_3D.GetLength(0);
            int dimYb = _data2_3D.GetLength(1);
            int dimZb = _data2_3D.GetLength(2);

            if (dimZa != dimZb)
                throw new InvalidOperationException(
                    $"Os arquivos possuem quantidades diferentes de canais espectrais ({dimZa} e {dimZb}).");

            int offX = (int)numOffsetX.Value;
            int offY = (int)numOffsetY.Value;

            int minX = Math.Min(0, offX);
            int minY = Math.Min(0, offY);
            int maxX = Math.Max(dimXa, offX + dimXb);
            int maxY = Math.Max(dimYa, offY + dimYb);

            int dimXc = maxX - minX;
            int dimYc = maxY - minY;

            long elementCount = (long)dimXc * dimYc * dimZa;

            if (elementCount <= 0 || elementCount > int.MaxValue)
                throw new InvalidOperationException(
                    "O cubo resultante é grande demais para ser alocado em memória.");

            double[,,] result = new double[dimXc, dimYc, dimZa];

            int startXa = -minX;
            int startYa = -minY;
            int startXb = offX - minX;
            int startYb = offY - minY;

            // Copia o primeiro cubo, preservando apenas valores válidos.
            for (int x = 0; x < dimXa; x++)
            {
                for (int y = 0; y < dimYa; y++)
                {
                    int tx = startXa + x;
                    int ty = startYa + y;

                    for (int z = 0; z < dimZa; z++)
                    {
                        double valor = _data1_3D[x, y, z];

                        result[tx, ty, z] =
                            double.IsFinite(valor) && valor != 0.0
                            ? valor
                            : 0.0;
                    }
                }
            }

            // Insere o segundo cubo:
            // preenche ausências e preserva o primeiro nas sobreposições válidas.
            for (int x = 0; x < dimXb; x++)
            {
                for (int y = 0; y < dimYb; y++)
                {
                    int tx = startXb + x;
                    int ty = startYb + y;

                    bool temMedicaoA = false;
                    bool temMedicaoB = false;

                    for (int z = 0; z < dimZa; z++)
                    {
                        double a = result[tx, ty, z];
                        double b = _data2_3D[x, y, z];

                        if (double.IsFinite(a) && a != 0.0)
                            temMedicaoA = true;

                        if (double.IsFinite(b) && b != 0.0)
                            temMedicaoB = true;
                    }

                    for (int z = 0; z < dimZa; z++)
                    {
                        double a = result[tx, ty, z];
                        double b = _data2_3D[x, y, z];

                        bool validoA = double.IsFinite(a) && a != 0.0;
                        bool validoB = double.IsFinite(b) && b != 0.0;

                        if (!temMedicaoA && temMedicaoB)
                        {
                            // A está ausente: usa B.
                            result[tx, ty, z] = validoB ? b : 0.0;
                        }
                        else if (temMedicaoA && !temMedicaoB)
                        {
                            // B está ausente: mantém A.
                            result[tx, ty, z] = validoA ? a : 0.0;
                        }
                        else if (temMedicaoA && temMedicaoB)
                        {
                            // Ambos têm medição: prioriza A.
                            // Se A não tiver valor válido neste canal, usa B.
                            result[tx, ty, z] = validoA ? a : (validoB ? b : 0.0);
                        }
                        else
                        {
                            // Nenhum dos cubos tem medição.
                            result[tx, ty, z] = 0.0;
                        }
                    }
                }
            }


            // Remove linhas horizontais que não possuem nenhuma medição
            // em nenhum pixel nem canal espectral.
            List<int> linhasComMedicao = new List<int>();

            for (int y = 0; y < dimYc; y++)
            {
                bool linhaTemMedicao = false;

                for (int x = 0; x < dimXc && !linhaTemMedicao; x++)
                {
                    for (int z = 0; z < dimZa; z++)
                    {
                        double valor = result[x, y, z];

                        if (double.IsFinite(valor) && valor != 0.0)
                        {
                            linhaTemMedicao = true;
                            break;
                        }
                    }
                }

                if (linhaTemMedicao)
                    linhasComMedicao.Add(y);
            }

            // Se não houver linhas vazias, retorna o cubo original.
            if (linhasComMedicao.Count == dimYc)
                return result;

            // Cria um cubo sem as linhas totalmente vazias.
            double[,,] cuboFinal = new double[
                dimXc,
                linhasComMedicao.Count,
                dimZa];

            for (int novoY = 0; novoY < linhasComMedicao.Count; novoY++)
            {
                int yOriginal = linhasComMedicao[novoY];

                for (int x = 0; x < dimXc; x++)
                {
                    for (int z = 0; z < dimZa; z++)
                    {
                        cuboFinal[x, novoY, z] =
                            result[x, yOriginal, z];
                    }
                }
            }

            return cuboFinal;

        }


        private string DiagnosticarCubo(double[,,] cubo, string nome)
{
    if (cubo == null)
        return $"{nome}: cubo não carregado.";

    int dimX = cubo.GetLength(0);
    int dimY = cubo.GetLength(1);
    int dimZ = cubo.GetLength(2);

    long zeros = 0;
    long valoresValidos = 0;
    long nan = 0;
    long infinitos = 0;
    long pixelsTodosZero = 0;
    long pixelsTodosInvalidos = 0;

    double minimo = double.PositiveInfinity;
    double maximo = double.NegativeInfinity;

    for (int x = 0; x < dimX; x++)
    {
        for (int y = 0; y < dimY; y++)
        {
            bool todosZero = true;
            bool todosInvalidos = true;

            for (int z = 0; z < dimZ; z++)
            {
                double v = cubo[x, y, z];

                if (double.IsNaN(v))
                {
                    nan++;
                    continue;
                }

                if (double.IsInfinity(v))
                {
                    infinitos++;
                    continue;
                }

                valoresValidos++;
                todosInvalidos = false;

                if (v == 0.0)
                {
                    zeros++;
                }
                else
                {
                    todosZero = false;
                }

                if (v < minimo) minimo = v;
                if (v > maximo) maximo = v;
            }

            if (todosZero)
                pixelsTodosZero++;

            if (todosInvalidos)
                pixelsTodosInvalidos++;
        }
    }

    return
        $"{nome}\n" +
        $"Dimensões: {dimX} × {dimY} × {dimZ}\n" +
        $"Valores iguais a zero: {zeros:N0}\n" +
        $"Valores finitos: {valoresValidos:N0}\n" +
        $"NaN: {nan:N0}\n" +
        $"Infinitos: {infinitos:N0}\n" +
        $"Pixels com todos os canais zero: {pixelsTodosZero:N0}\n" +
        $"Pixels sem valores finitos: {pixelsTodosInvalidos:N0}\n" +
        $"Mínimo finito: " +
        $"{(valoresValidos > 0 ? minimo.ToString("G6") : "N/A")}\n" +
        $"Máximo finito: " +
        $"{(valoresValidos > 0 ? maximo.ToString("G6") : "N/A")}";
}



        private void BtnExportHdf5_Click(object sender, EventArgs e)
        {
            double[,,] combinedCube3D;

            try
            {
                combinedCube3D = GenerateStitchedCube3D();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível gerar o mosaico 3D:\n\n" + ex.Message,
                    "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (combinedCube3D == null)
            {
                MessageBox.Show("O cubo 3D não foi gerado.", "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int dimX = combinedCube3D.GetLength(0);
            int dimY = combinedCube3D.GetLength(1);
            int dimZ = combinedCube3D.GetLength(2);

            if (dimX <= 0 || dimY <= 0 || dimZ <= 0)
            {
                MessageBox.Show($"Dimensões inválidas: [{dimX}, {dimY}, {dimZ}].", "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Arquivo HDF5 (*.h5;*.hdf5)|*.h5;*.hdf5",
                FileName = "Mosaico_Stitched_3D.h5",
                DefaultExt = "h5",
                AddExtension = true,
                OverwritePrompt = true
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                long fileId = -1;
                long groupId = -1;
                long fileSpaceId = -1;
                long memSpaceId = -1;
                long datasetId = -1;
                GCHandle pinnedHandle = default(GCHandle);
                bool exportSucceeded = false;

                try
                {
                    H5.open();

                    fileId = H5F.create(sfd.FileName, H5F.ACC_TRUNC, H5P.DEFAULT, H5P.DEFAULT);
                    if (fileId < 0)
                        throw new InvalidOperationException("Não foi possível criar o arquivo HDF5.");

                    groupId = H5G.create(fileId, "XRF", H5P.DEFAULT, H5P.DEFAULT, H5P.DEFAULT);
                    if (groupId < 0)
                        throw new InvalidOperationException("Não foi possível criar o grupo /XRF.");

                    ulong[] fileDims = { (ulong)dimX, (ulong)dimY, (ulong)dimZ };
                    fileSpaceId = H5S.create_simple(3, fileDims, null);
                    if (fileSpaceId < 0)
                        throw new InvalidOperationException("Não foi possível criar o espaço de dados 3D.");

                    datasetId = H5D.create(groupId, "Spectra", H5T.NATIVE_DOUBLE,
                        fileSpaceId, H5P.DEFAULT, H5P.DEFAULT, H5P.DEFAULT);
                    if (datasetId < 0)
                        throw new InvalidOperationException("Não foi possível criar o dataset /XRF/Spectra.");

                    // Um único dataset 3D é criado. A escrita em fatias é apenas interna;
                    // não divide o mosaico em arquivos ou datasets separados.
                    // Cada chamada escreve uma fatia X completa: [1, dimY, dimZ].
                    ulong[] memDims = { 1UL, (ulong)dimY, (ulong)dimZ };
                    memSpaceId = H5S.create_simple(3, memDims, null);
                    if (memSpaceId < 0)
                        throw new InvalidOperationException("Não foi possível criar o espaço de memória para a gravação em blocos.");

                    // Usa um buffer 1D temporário para uma única fatia X.
                    // Isso evita calcular deslocamentos dentro do ponteiro de um array CLR 3D grande.
                    // O arquivo final continua sendo um único dataset [X, Y, Z].
                    int sliceElementCount = checked(dimY * dimZ);
                    double[] sliceBuffer = new double[sliceElementCount];
                    pinnedHandle = GCHandle.Alloc(sliceBuffer, GCHandleType.Pinned);
                    IntPtr slicePointer = pinnedHandle.AddrOfPinnedObject();
                    if (slicePointer == IntPtr.Zero)
                        throw new InvalidOperationException("Não foi possível fixar o buffer temporário da fatia.");

                    ulong[] count = { 1UL, (ulong)dimY, (ulong)dimZ };

                    for (int x = 0; x < dimX; x++)
                    {
                        // Copia somente a fatia atual para memória contígua [Y * Z].
                        int index = 0;
                        for (int y = 0; y < dimY; y++)
                        {
                            for (int z = 0; z < dimZ; z++)
                                sliceBuffer[index++] = combinedCube3D[x, y, z];
                        }

                        ulong[] start = { (ulong)x, 0UL, 0UL };
                        int selectStatus = H5S.select_hyperslab(
                            fileSpaceId, H5S.seloper_t.SET, start, null, count, null);
                        if (selectStatus < 0)
                            throw new InvalidOperationException(
                                $"Não foi possível selecionar a fatia X={x} para gravação.");

                        int writeStatus = H5D.write(
                            datasetId,
                            H5T.NATIVE_DOUBLE,
                            memSpaceId,
                            fileSpaceId,
                            H5P.DEFAULT,
                            slicePointer);

                        if (writeStatus < 0)
                            throw new InvalidOperationException(
                                $"H5D.write falhou na fatia X={x} de {dimX}. Código: {writeStatus}. " +
                                "A fatia foi copiada para um buffer 1D contíguo antes da gravação.");
                    }

                    // Libera o buffer fixado antes de reabrir o arquivo para validação.
                    pinnedHandle.Free();
                    H5S.close(memSpaceId); memSpaceId = -1;
                    H5D.close(datasetId); datasetId = -1;
                    H5S.close(fileSpaceId); fileSpaceId = -1;
                    H5G.close(groupId); groupId = -1;

                    int closeStatus = H5F.close(fileId);
                    fileId = -1;
                    if (closeStatus < 0)
                        throw new InvalidOperationException("Não foi possível finalizar corretamente o arquivo HDF5.");

                    ValidateExportedCube(sfd.FileName, combinedCube3D);
                    exportSucceeded = true;

                    MessageBox.Show(
                        "Mosaico 3D exportado e reaberto com sucesso!\n\n" +
                        $"Arquivo: {sfd.FileName}\n" +
                        "Dataset: /XRF/Spectra\n" +
                        $"Dimensões: {dimX} × {dimY} × {dimZ}\n" +
                        "Os blocos foram gravados em um único dataset tridimensional.",
                        "Exportação concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Falha na exportação do HDF5:\n\n" + ex.Message +
                        "\n\nO arquivo pode estar incompleto. Não o utilize.",
                        "Erro ao exportar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (pinnedHandle.IsAllocated) pinnedHandle.Free();
                    if (memSpaceId >= 0) H5S.close(memSpaceId);
                    if (datasetId >= 0) H5D.close(datasetId);
                    if (fileSpaceId >= 0) H5S.close(fileSpaceId);
                    if (groupId >= 0) H5G.close(groupId);
                    if (fileId >= 0) H5F.close(fileId);

                    if (!exportSucceeded)
                    {
                        try
                        {
                            if (File.Exists(sfd.FileName)) File.Delete(sfd.FileName);
                        }
                        catch
                        {
                            // Se o arquivo estiver bloqueado, a mensagem de erro acima informa que ele é inválido.
                        }
                    }
                }
            }
        }

        private void ValidateExportedCube(string fileName, double[,,] expected)
        {
            H5.open();

            long fileId = -1;
            long datasetId = -1;
            long spaceId = -1;

            try
            {
                fileId = H5F.open(fileName, H5F.ACC_RDONLY);
                if (fileId < 0)
                    throw new InvalidOperationException("O arquivo exportado não pôde ser reaberto.");

                datasetId = H5D.open(fileId, "/XRF/Spectra");
                if (datasetId < 0)
                    throw new InvalidOperationException("O dataset /XRF/Spectra não foi encontrado após a gravação.");

                spaceId = H5D.get_space(datasetId);
                int rank = H5S.get_simple_extent_ndims(spaceId);

                if (rank != 3)
                    throw new InvalidOperationException(
                        $"O dataset exportado possui rank {rank}, mas deveria ser 3D.");

                ulong[] dims = new ulong[3];
                H5S.get_simple_extent_dims(spaceId, dims, null);

                if (dims[0] != (ulong)expected.GetLength(0) ||
                    dims[1] != (ulong)expected.GetLength(1) ||
                    dims[2] != (ulong)expected.GetLength(2))
                {
                    throw new InvalidOperationException(
                        $"As dimensões gravadas ({dims[0]} × {dims[1]} × {dims[2]}) " +
                        "não correspondem às dimensões do cubo gerado.");
                }
            }
            finally
            {
                if (spaceId >= 0) H5S.close(spaceId);
                if (datasetId >= 0) H5D.close(datasetId);
                if (fileId >= 0) H5F.close(fileId);
            }

            // Não carregamos o cubo inteiro uma segunda vez durante a validação:
            // para mosaicos grandes isso poderia exigir mais 2 GB de memória.
            // A validação acima confirma que /XRF/Spectra existe e tem rank e dimensões corretos.
        }

        private void BtnExportPng_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Imagem PNG (*.png)|*.png",
                FileName = "Mosaico_XRF.png",
                AddExtension = true,
                DefaultExt = "png",
                OverwritePrompt = true
            })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        formsPlot1.Plot.SavePng(sfd.FileName, 1920, 1080);
                        MessageBox.Show("Mosaico PNG exportado com sucesso!",
                            "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Erro ao exportar PNG:\n" + ex.Message,
                            "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void BtnOpenFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Hierarchical Data Format 5 (*.hdf5;*.h5)|*.hdf5;*.h5"
            })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    ttbFileName.Text = ofd.FileName;

                    // Ao escolher um novo arquivo principal, limpa o segundo arquivo antigo.
                    ttbSecondFileName.Clear();
                    _data1_3D = null;
                    _data2_3D = null;
                    _data1_2D = null;
                    _data2_2D = null;

                    ShowDatasets(ttbFileName.Text);
                    formsPlot1.Plot.Clear();
                    formsPlot1.Refresh();
                }
            }
        }

        private void BtnOpenSecondFile_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ttbFileName.Text) || !File.Exists(ttbFileName.Text))
            {
                MessageBox.Show("Carregue primeiro o arquivo HDF5 principal.",
                    "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Hierarchical Data Format 5 (*.hdf5;*.h5)|*.hdf5;*.h5"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                ttbSecondFileName.Text = ofd.FileName;

                if (dataGridView1.SelectedCells.Count > 0 &&
                    dataGridView1.SelectedCells[0].Value != null)
                {
                    string dataset = Convert.ToString(dataGridView1.SelectedCells[0].Value);
                    ReadHDF(ttbFileName.Text, dataset);
                }
                else
                {
                    MessageBox.Show("Selecione um dataset na lista antes de carregar o segundo arquivo.",
                        "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            formsPlot1.Plot.HideGrid();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ttbFileName.Text) || !File.Exists(ttbFileName.Text))
            {
                MessageBox.Show("Carregue um arquivo HDF5 primeiro.",
                    "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (dataGridView1.SelectedCells.Count == 0 ||
                dataGridView1.SelectedCells[0].Value == null)
            {
                MessageBox.Show("Selecione um dataset para visualizar.",
                    "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dataset = Convert.ToString(dataGridView1.SelectedCells[0].Value);
            button3.Enabled = false;

            try
            {
                ReadHDF(ttbFileName.Text, dataset);
            }
            finally
            {
                button3.Enabled = true;
            }
        }
    }
}
