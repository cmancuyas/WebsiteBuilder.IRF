using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class MediaAsset : BaseModel
    {
        [Key]
        public int Id { get; set; }
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;
        [MaxLength(255)]
        public string ContentType { get; set; } = string.Empty;
        [MaxLength(255)]
        public string SizeBytes { get; set; } = string.Empty;
        [MaxLength(500)]
        public string StorageKey { get; set; } = string.Empty;
        [MaxLength(500)]
        public string Width { get; set; } = string.Empty;
        [MaxLength(500)]
        public string Height { get; set; } = string.Empty;
        [MaxLength(1000)]
        public string AltText { get; set; } = string.Empty;
        [MaxLength(64)]
        public string CheckSum { get; set; } = string.Empty;

    }
}
