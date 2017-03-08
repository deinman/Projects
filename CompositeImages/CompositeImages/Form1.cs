using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows.Forms;

namespace CompositeImages
{
	public partial class Form1 : Form
	{
		// Enables the UI to signal cancellation to the network.
		private CancellationTokenSource cancellationTokenSource;

		// The head of the dataflow network.
		private ITargetBlock<string> headBlock;

		public Form1()
		{
			InitializeComponent();
		}

		// Creates the image processing dataflow network and returns the
		//	head node of the network.
		private ITargetBlock<string> CreateImageProcessingNetwork()
		{
			//
			// Create the dataflow blocks that form the network.
			// 

			// Create a dataflow block that takes a folder path as input
			// and returns a collection of Bitmap objects.
			var loadBitmaps = new TransformBlock<string, IEnumerable<Bitmap>>(path =>
			{
				try
				{
					return LoadBitmaps(path);
				}
				catch (OperationCanceledException)
				{
					// Handle cancellation by passing the empty collection 
					// to the next stage of the network.
					return Enumerable.Empty<Bitmap>();
				}
			});

			// Create a dataflow block that takes a collection of Bitmap objects
			// and returns a single composite bitmap.
			var createCompositeBitmap = new TransformBlock<IEnumerable<Bitmap>, Bitmap>(bitmaps =>
			{
				try
				{
					return CreateCompositeBitmap(bitmaps);
				}
				catch (OperationCanceledException)
				{
					// Handle cancellation by passing null to the next stage
					// of the network.
					return null;
				}
			});

			// Create a dataflow block that displays the provided bitmap on the form.
			var displayCompositeBitmap = new ActionBlock<Bitmap>(bitmap =>
				{
					// Display the bitmap.
					pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
					pictureBox1.Image = bitmap;

					// Enable the user to select another folder.
					buttonChooseFolder.Enabled = true;
					buttonCancel.Enabled = false;
					Cursor = DefaultCursor;
				},
				// Specify a task scheduler from the current synchronization context
				// so that the action runs on the UI thread.
				new ExecutionDataflowBlockOptions {TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext()});

			// Create a dataflow block that responds to a cancellation request by
			// displaying an image to indicate that the operation is canceled and
			// enables the user to select another folder.
			var operationCancelled = new ActionBlock<object>(delegate
				{
					// Display the error image to indicate that the operation
					// was canceled.
					pictureBox1.SizeMode = PictureBoxSizeMode.CenterImage;
					pictureBox1.Image = pictureBox1.ErrorImage;

					// Enable the user to select another folder.
					buttonChooseFolder.Enabled = true;
					buttonChooseFolder.Enabled = false;
					Cursor = DefaultCursor;
				},
				// Specify a task scheduler from the current synchronization context
				// so that the action runs on the UI thread.
				new ExecutionDataflowBlockOptions {TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext()});

			//
			// Connect the network.
			//

			// Link loadBitmaps to createCompositeBitmap.
			// The provided predicate ensures that the createCompositeBitmap accepts the
			// collection of bitmaps only if that collection has at least one member.
			loadBitmaps.LinkTo(createCompositeBitmap, bitmaps => bitmaps.Any());

			// Also link loadBitmaps to operationCancelled.
			// When createCompositeBitmap rejects the message, loadBitmaps
			// offers the message to operationCancelled.
			// operationCancelled accepts all messages because we do not provide a predicate.
			loadBitmaps.LinkTo(operationCancelled);

			// Link createCompositeBitmap to displayCompositeBitmap.
			// The provided predicate ensures that displayCompositeBitmap accepts the
			// bitmap only if it is non-null.
			createCompositeBitmap.LinkTo(displayCompositeBitmap, bitmap => bitmap != null);

			// Also link createCompositeBitmap to operationCancelled.
			// When displayCompositeBitmap rejects the message, createCompositeBitmap
			// offers the message to operationCancelled.
			// operationCancelled accepts all messages because we do not provide a predicate.
			createCompositeBitmap.LinkTo(operationCancelled);

			// Return the head of the network.
			return loadBitmaps;
		}

		// Loads all bitmap files that exist at the provided path.
		private IEnumerable<Bitmap> LoadBitmaps(string path)
		{
			var bitmaps = new List<Bitmap>();

			// Load a variety of image types.
			foreach (var bitmapType in new[] {"*.bmp", "*.gif", "*.jpg", "*.png", "*.tif"})
			{
				// Load each bitmap for the current extension.
				foreach (var fileName in Directory.GetFiles(path, bitmapType))
				{
					// Throw OperationCancelledException if cancellation is requested.
					cancellationTokenSource.Token.ThrowIfCancellationRequested();

					try
					{
						// Add the Bitmap object to the collection.
						bitmaps.Add(new Bitmap(fileName));
					}
					catch (Exception)
					{
						// TODO: A complete application should handle the error.
					}
				}
			}
			return bitmaps;
		}

		// Creates a composite bitmap from the provided collection of Bitmap objects.
		// This method computes the average color of each pixel among all bitmaps
		// to create the composite image.
		private Bitmap CreateCompositeBitmap(IEnumerable<Bitmap> bitmaps)
		{
			var bitmapArray = bitmaps.ToArray();

			// Compute the max width and height components
			// of all bitmaps in the collection.
			var largest = new Rectangle();
			foreach (Bitmap bitmap in bitmapArray)
			{
				if (bitmap.Width > largest.Width)
				{
					largest.Width = bitmap.Width;
				}
				if (bitmap.Height > largest.Height)
				{
					largest.Height = bitmap.Height;
				}
			}

			// Create a 32-bit Bitmap object with the greatest dimensions.
			var result = new Bitmap(largest.Width, largest.Height, PixelFormat.Format32bppArgb);

			// Lock the result Bitmap.
			BitmapData resultBitmapData = result.LockBits(new Rectangle(new Point(), result.Size), ImageLockMode.WriteOnly, result.PixelFormat);

			// Lock each source bitmap to create a parallel list of BitmapData objects.
			var bitmapDataList = (from bitmap in bitmapArray select bitmap.LockBits(new Rectangle(new Point(), bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)).ToList();

			// Compute each column in parallel.
			Parallel.For(0, largest.Width, new ParallelOptions {CancellationToken = cancellationTokenSource.Token}, i =>
			{
				// Compute each row.
				for (var j = 0; j < largest.Height; j++)
				{
					// Counts the number of bitmaps whose dimensions
					// contain the current location.
					var count = 0;

					// The sum of all alpha, red, green, and blue components.
					int a = 0, r = 0, g = 0, b = 0;

					// For each bitmap, compute the sum of all color components.
					foreach (BitmapData bitmapData in bitmapDataList)
					{
						// Ensure that we stay within the bounds of the image.
						if (bitmapData.Width > i && bitmapData.Height > j)
						{
							unsafe
							{
								var row = (byte*) (bitmapData.Scan0 + j * bitmapData.Stride);
								var pix = row + 4 * i;
								a += *pix;
								pix++;
								r += *pix;
								pix++;
								g += *pix;
								pix++;
								b += *pix;
							}
							count++;
						}
					}

					unsafe
					{
						// Compute the average of each color component.
						a /= count;
						r /= count;
						g /= count;
						b /= count;

						// Set the result pixel.
						var row = (byte*) (resultBitmapData.Scan0 + j * resultBitmapData.Stride);
						var pix = row + 4 * i;
						*pix = (byte) a;
						pix++;
						*pix = (byte) r;
						pix++;
						*pix = (byte) g;
						pix++;
						*pix = (byte) b;
					}
				}
			});

			// Unlock the source bitmaps.
			for (var i = 0; i < bitmapArray.Length; i++)
			{
				bitmapArray[i].UnlockBits(bitmapDataList[i]);
			}

			// Unlock the result bitmap.
			result.UnlockBits(resultBitmapData);

			// Return the result.
			return result;
		}

		private void buttonChooseFolder_Click(object sender, EventArgs e)
		{
			// Create a FolderBrowserDialog object to enable the user to
			// select a folder.
			var dlg = new FolderBrowserDialog {ShowNewFolderButton = false};

			// Set the selected path to the common Sample Pictures folder
			// if it exists.
			var initialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "Sample Pictures");
			if (Directory.Exists(initialDirectory))
			{
				dlg.SelectedPath = initialDirectory;
			}

			// Show the dialog and process the dataflow network.
			if (dlg.ShowDialog() == DialogResult.OK)
			{
				// Create a new CancellationTokenSource object to enable cancellation.
				cancellationTokenSource = new CancellationTokenSource();

				// Create the image processing network if needed.
				if (headBlock == null)
				{
					headBlock = CreateImageProcessingNetwork();
				}

				// Post the selected path to the network.
				headBlock.Post(dlg.SelectedPath);

				// Enable the Cancel button & disable the Choose Folder button.
				buttonChooseFolder.Enabled = false;
				buttonCancel.Enabled = true;

				// Show a wait cursor.
				Cursor = Cursors.WaitCursor;
			}
		}

		private void buttonCancel_Click(object sender, EventArgs e)
		{
			// Signal the request for cancellation. The current component of
			// the dataflow network will respond to the cancellation request.
			cancellationTokenSource.Cancel();
		}
	}
}