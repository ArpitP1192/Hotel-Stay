using System;
using HotelStay.Contracts.Models;

namespace HotelStay.Api.Providers;
public record HotelSearchQuery(
    string Destination,
    DateOnly CheckIn,
    DateOnly CheckOut,
    RoomType? RoomType
);
