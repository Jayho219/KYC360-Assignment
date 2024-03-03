using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace trackingapi.Models
{
    public interface IEntity
    {
        public List<Address>? Addresses { get; set; }
        public List<Date> Dates { get; set; }
        public bool Deceased { get; set; }
        public string? Gender { get; set; }
        public string Id { get; set; }
        public List<Name> Names { get; set; }
    }



    public class Address
    {
        [Key]
        public string Id { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
    }

  


    public class Date
    {
        [Key]
        public string Id { get; set; }
        public string? DateType { get; set; }
        public DateTime? DateValue { get; set; }
    }


    public class Name
    {
        [Key]
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }


    public class Entity : IEntity
    {
        public List<Address>? Addresses { get; set; }
        public List<Date> Dates { get; set; }
        public bool Deceased { get; set; }
        public string? Gender { get; set; }
        public string Id { get; set; }
        public List<Name> Names { get; set; }
    }
}