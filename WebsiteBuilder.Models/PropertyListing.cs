using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class PropertyListing : BaseModel
    {
        [Key]
        public int Id { get; set; }
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        [Required]
        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;
        public double Price { get; set; }
        public int CurrencyId { get; set; }
        public int ListingTypeId { get; set; }
        public int PropertyTypeId { get; set; }
        [MaxLength(200)]
        public string Bedrooms { get; set; } = string.Empty;
        [MaxLength(200)]
        public string Batrooms { get; set; } = string.Empty;
        [MaxLength(200)]
        public string FloorArea { get; set; } = string.Empty;
        [MaxLength(200)]
        public string LotArea { get; set; } = string.Empty;
        public int PropertyStatusId { get; set; }

        public Currency? Currency { get; set; }
        public ListingType? ListingType { get; set; }
        public PropertyType? PropertyType { get; set; }
    }
}
