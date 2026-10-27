using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Vistas;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Punto_de_Venta
{
    public partial class CodigosBarrasWindow : UserControl
    {
        private List<Producto> _catalogo = new();
        public ObservableCollection<Producto> ResultadosBusqueda { get; set; } = new();
        public ObservableCollection<EtiquetaImpresion> ColaImpresion { get; set; } = new();

        private FixedDocument? _documentoActual;

        public CodigosBarrasWindow()
        {
            InitializeComponent();
            SearchResultsList.ItemsSource = ResultadosBusqueda;
            PrintQueueGrid.ItemsSource = ColaImpresion;
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    this.Focus();
                    Keyboard.Focus(SearchBox);
                    SearchBox?.Focus();
                    SearchBox?.SelectAll();
                    FocusSearch();
                }));
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _catalogo = ProductoDB.Load() ?? new();
                FocusSearch();
            }
            catch
            {
                CustomMessageBox.Show("Error al cargar el catálogo de productos.");
            }
        }
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FocusSearch();
        }
        private void FocusSearch()
        {
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (SearchBox != null)
                {
                    this.Focus();
                    SearchBox.Focus();
                    Keyboard.Focus(SearchBox);
                }
            }));
        }
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RegresarAlMenu();
            }
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Visibility = Visibility.Collapsed;
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RegresarAlMenu();
                }
                e.Handled = true;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim();
            ResultadosBusqueda.Clear();

            if (string.IsNullOrEmpty(query)) return;

            var matches = _catalogo.Where(p =>
                (!string.IsNullOrEmpty(p.Codigo) && p.Codigo.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.Nombre) && p.Nombre.Contains(query, StringComparison.OrdinalIgnoreCase))
            ).Take(20);

            foreach (var match in matches)
            {
                ResultadosBusqueda.Add(match);
            }
        }

        private void SearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AddSelectedProduct();
        }

        // ================= NUEVOS EVENTOS PARA CANTIDAD =================
        private void BtnDecreaseQty_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(QuantityBox.Text, out int qty) && qty > 1)
            {
                QuantityBox.Text = (qty - 1).ToString();
            }
        }

        private void BtnIncreaseQty_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(QuantityBox.Text, out int qty))
            {
                QuantityBox.Text = (qty + 1).ToString();
            }
            else
            {
                QuantityBox.Text = "1";
            }
        }

        private void AddToList_Click(object sender, RoutedEventArgs e)
        {
            AddSelectedProduct();
        }

        private void AddSelectedProduct()
        {
            if (SearchResultsList.SelectedItem is Producto prod)
            {
                if (string.IsNullOrWhiteSpace(prod.Codigo) || prod.Codigo.Length < 2)
                {
                    CustomMessageBox.Show("Este producto no tiene un código de barras válido asignado.", "Código Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(QuantityBox.Text, out int qty) || qty <= 0)
                {
                    qty = 1;
                }

                var existente = ColaImpresion.FirstOrDefault(x => x.Producto.Id == prod.Id);
                if (existente != null)
                {
                    existente.Cantidad += qty;
                    PrintQueueGrid.Items.Refresh(); // Fuerza a la cuadrícula a ver el nuevo número
                }
                else
                {
                    ColaImpresion.Add(new EtiquetaImpresion { Producto = prod, Cantidad = qty });
                }

                SearchBox.Clear();
                QuantityBox.Text = "1";
                SearchBox.Focus();
            }
        }

        // ================= NUEVO EVENTO ELIMINAR INDIVIDUAL =================
        private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is EtiquetaImpresion item)
            {
                ColaImpresion.Remove(item);
                if (ColaImpresion.Count == 0)
                {
                    DocViewer.Document = null;
                    DocPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        private void ClearQueue_Click(object sender, RoutedEventArgs e)
        {
            ColaImpresion.Clear();
            DocViewer.Document = null;
            DocPlaceholder.Visibility = Visibility.Visible;
        }

        private void GeneratePreview_Click(object sender, RoutedEventArgs e)
        {
            if (ColaImpresion.Count == 0)
            {
                CustomMessageBox.Show("Agrega al menos un producto a la cola de impresión.", "Aviso");
                return;
            }

            _documentoActual = new FixedDocument();
            Size pageSize = new Size(794, 1123);

            int columnas = 3;
            int filas = 10;
            double margenX = 20;
            double margenY = 30;

            double anchoEtiqueta = (pageSize.Width - (margenX * 2)) / columnas;
            double altoEtiqueta = (pageSize.Height - (margenY * 2)) / filas;

            var itemsAImprimir = new List<Producto>();
            foreach (var item in ColaImpresion)
            {
                for (int i = 0; i < item.Cantidad; i++)
                {
                    itemsAImprimir.Add(item.Producto);
                }
            }

            int itemIndex = 0;
            while (itemIndex < itemsAImprimir.Count)
            {
                FixedPage page = new FixedPage
                {
                    Width = pageSize.Width,
                    Height = pageSize.Height,
                    Background = Brushes.White
                };

                for (int f = 0; f < filas; f++)
                {
                    for (int c = 0; c < columnas; c++)
                    {
                        if (itemIndex >= itemsAImprimir.Count) break;

                        Producto prod = itemsAImprimir[itemIndex];

                        Border etiquetaBorder = new Border
                        {
                            Width = anchoEtiqueta,
                            Height = altoEtiqueta,
                            Padding = new Thickness(10),
                        };

                        StackPanel panelInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                        TextBlock txtNombre = new TextBlock
                        {
                            Text = prod.Nombre,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = Brushes.Black,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            MaxHeight = 32,
                            Margin = new Thickness(0, 0, 0, 4)
                        };

                        Canvas canvasBarcode = GenerarVectorCodigo39(prod.Codigo);
                        Viewbox vbBarcode = new Viewbox
                        {
                            Child = canvasBarcode,
                            Height = 40,
                            Margin = new Thickness(0, 4, 0, 4),
                            Stretch = Stretch.Uniform
                        };

                        TextBlock txtCodigo = new TextBlock
                        {
                            Text = prod.Codigo,
                            FontSize = 10,
                            Foreground = Brushes.Black,
                            TextAlignment = TextAlignment.Center,
                            FontFamily = new FontFamily("Consolas")
                        };

                        TextBlock txtPrecio = new TextBlock
                        {
                            Text = prod.Precio.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("es-MX")),
                            FontSize = 14,
                            FontWeight = FontWeights.ExtraBold,
                            Foreground = Brushes.Black,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 4, 0, 0)
                        };

                        panelInfo.Children.Add(txtNombre);
                        panelInfo.Children.Add(vbBarcode);
                        panelInfo.Children.Add(txtCodigo);
                        panelInfo.Children.Add(txtPrecio);
                        etiquetaBorder.Child = panelInfo;

                        FixedPage.SetLeft(etiquetaBorder, margenX + (c * anchoEtiqueta));
                        FixedPage.SetTop(etiquetaBorder, margenY + (f * altoEtiqueta));

                        page.Children.Add(etiquetaBorder);
                        itemIndex++;
                    }
                }

                PageContent pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                _documentoActual.Pages.Add(pageContent);
            }

            DocPlaceholder.Visibility = Visibility.Collapsed;
            DocViewer.Document = _documentoActual;
            DocViewer.FitToWidth();
        }

        private void PrintDocument_Click(object sender, RoutedEventArgs e)
        {
            if (_documentoActual == null)
            {
                CustomMessageBox.Show("Primero genera la plantilla antes de imprimir.", "Aviso");
                return;
            }

            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                printDialog.PrintDocument(_documentoActual.DocumentPaginator, "Impresión de Etiquetas A4");
            }
        }

        private Canvas GenerarVectorCodigo39(string textoOriginal)
        {
            string validChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%*";
            string[] patterns = {
                "000110100", "100100001", "001100001", "101100000", "000110001", "100110000", "001110000", "000100101", "100100100", "001100100",
                "100001001", "001001001", "101001000", "000011001", "100011000", "001011000", "000001101", "100001100", "001001100", "000011100",
                "100000011", "001000011", "101000010", "000010011", "100010010", "001010010", "000000111", "100000110", "001000110", "000010110",
                "110000001", "011000001", "111000000", "010010001", "110010000", "011010000", "010000101", "110000100", "011000100", "010101000",
                "010100010", "010001010", "000101010", "010010100"
            };

            string texto = "*" + textoOriginal.ToUpper().Replace("*", "") + "*";

            Canvas canvas = new Canvas { Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
            double x = 0;
            double narrow = 1.5;
            double wide = 3.5;

            foreach (char c in texto)
            {
                int index = validChars.IndexOf(c);
                if (index < 0) continue;

                string pattern = patterns[index];
                for (int i = 0; i < 9; i++)
                {
                    bool isBar = (i % 2 == 0);
                    bool isWide = pattern[i] == '1';
                    double w = isWide ? wide : narrow;

                    if (isBar)
                    {
                        Rectangle rect = new Rectangle
                        {
                            Width = w,
                            Height = 40,
                            Fill = Brushes.Black,
                            SnapsToDevicePixels = true
                        };
                        Canvas.SetLeft(rect, x);
                        canvas.Children.Add(rect);
                    }
                    x += w;
                }
                x += narrow;
            }

            canvas.Width = x;
            return canvas;
        }
    }

    public class EtiquetaImpresion
    {
        public Producto Producto { get; set; } = null!;
        public int Cantidad { get; set; }
    }
}