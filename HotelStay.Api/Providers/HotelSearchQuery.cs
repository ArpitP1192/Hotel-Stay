using System;
using HotelStay.Api.Models;

namespace HotelStay.Api.Providers;
public record HotelSearchQuery(
    string Destination,
    DateOnly CheckIn,
    DateOnly CheckOut,
    RoomType? RoomType
);
