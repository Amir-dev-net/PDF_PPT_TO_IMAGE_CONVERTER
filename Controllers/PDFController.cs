using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Docnet.Core;
using Docnet.Core.Models;
using System.Drawing.Imaging;
using System.Collections.Concurrent;
using System.Drawing;

namespace WebApplicationPDF_Image.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PDFController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        // In-memory store of images per batch ID
        private static readonly ConcurrentDictionary<string, List<byte[]>> _imageStore = new();

        public PDFController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost]//Post Method
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File not provided.");

            var uploads = Path.Combine(_env.ContentRootPath, "uploads");
            Directory.CreateDirectory(uploads);

            var filePath = Path.Combine(uploads, Guid.NewGuid() + Path.GetExtension(file.FileName));

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Optional: Convert PPT to PDF
            if (Path.GetExtension(file.FileName).ToLower() is ".ppt" or ".pptx")
            {
                filePath = ConvertPptToPdf(filePath);
            }

            var batchId = Guid.NewGuid().ToString();
            var imageBytes = ConvertPdfToImagesInMemory(filePath);
            _imageStore[batchId] = imageBytes;

            var imageUrls = imageBytes.Select((_, i) =>
                $"{Request.Scheme}://{Request.Host}/api/pdf/image/{batchId}/{i + 1}"
            ).ToList();

            return Ok(new { batchId, images = imageUrls });
        }

        private List<byte[]> ConvertPdfToImagesInMemory(string pdfPath)
        {
            var images = new List<byte[]>();

            using var docReader = DocLib.Instance.GetDocReader(System.IO.File.ReadAllBytes(pdfPath), new PageDimensions(1080, 1920));
            var pageCount = docReader.GetPageCount();

            for (int i = 0; i < pageCount; i++)
            {
                using var pageReader = docReader.GetPageReader(i);
                var rawBytes = pageReader.GetImage();

                using var bmp = new Bitmap(pageReader.GetPageWidth(), pageReader.GetPageHeight(), PixelFormat.Format32bppArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
                System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, data.Scan0, rawBytes.Length);
                bmp.UnlockBits(data);

                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                images.Add(ms.ToArray());
            }

            return images;
        }

        // GET: api/pdf/image/{batchId}/{pageNumber}
        [HttpGet("image/{batchId}/{pageNumber}")]
        public IActionResult GetImageByBatch(string batchId, int pageNumber)
        {
            if (!_imageStore.TryGetValue(batchId, out var images) || pageNumber < 1 || pageNumber > images.Count)
            {
                return NotFound("Image not found.");
            }

            var imageData = images[pageNumber - 1];
            return File(imageData, "image/png"); // Ensure this is the correct usage of the File method.
        }

        private string ConvertPptToPdf(string pptPath)
        {
            // Not implemented yet
            throw new NotImplementedException("PPT to PDF conversion not implemented.");
        }
    }
}
