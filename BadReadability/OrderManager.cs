    namespace GDE247.BL.Concrete
    {
        public class OrderManager
        {
            private readonly MobstedApiHelper mobstedApiHelper;
            private readonly TaxiHelper taxiHelper;
            private readonly PinHelper pinHelper;
            private readonly ConvertOrderHelper convertOrderHelper;

            public OrderManager(MobstedApiHelper mobstedApiHelper, PinHelper pinHelper, ConvertOrderHelper convertOrderHelper, TaxiHelper taxiHelper)
            {
                this.mobstedApiHelper = mobstedApiHelper;
                this.taxiHelper = taxiHelper;
                this.pinHelper = pinHelper;
                this.convertOrderHelper = convertOrderHelper;
            }

            public async Task<string> CreateOrderWithoutDelivery(MerchantOrder merchantOrder, CourierInfo courier)
            {
                try
                {
                    await using var context = new GDEContext();
                    context.Database.BeginTransaction();

                    SaveMerchantOrderToDb(context, merchantOrder, null);
                    var verificationOrderId = await SaveVerificationOrderToDb(context, merchantOrder);
                    var courierDb = await GetCourier(courier, context);
                    var taxiOrderId = Guid.NewGuid();
                    var dbTaxiOrder = new CourierServiceOrder
                    {
                        Id = taxiOrderId,
                        TaxiId = courierDb.CourierServiceId ?? Guid.Empty,
                        AdditionalInfo = null
                    };
                    await SaveTaxiOrderToDb(context, dbTaxiOrder);
                    var orderId = Guid.NewGuid();
                    //save taxiorder id to db
                    var order = SaveOrderToDb(context, orderId, merchantOrder.Id, null, verificationOrderId,
                        courierDb.Id, 0, false);
                    LogWriter.LogWrite("order saved to db");
                    var link = await mobstedApiHelper.CreateObject(courierDb.User, order.Id,
                        MobstedApiHelper.CourierApplicationId, order.Id, verificationOrderId);
                    order.LinkToMobile = link;
                    context.Database.CommitTransaction();
                    context.SaveChanges();

                    return link;
                }
                catch (Exception e)
                {
                    LogWriter.LogWrite($"order without delivery error {e}");
                    throw;
                }
            }

            public async Task<List<Order>> List()
            {
                using var context = new GDEContext();
                var merchantOrders = context.Set<Repository.Model.MerchantOrder>().Include(o => o.Merchant).Select(mo =>
                    new MerchantOrder
                    {
                        Id = mo.Id,
                        PointId = mo.PointId,
                        Comission = mo.Merchant.Comission
                        //Address = new AddressInfo
                        //  {Address = context.Set<Point>().FirstOrDefault(p => p.Id == mo.PointId).Address}
                    });
                var orders = await context.Set<Repository.Model.Order>() //.Include(o => o.States)
                    .Select(o => new Order
                    {
                        Id = o.Id,
                        OrderNumber = o.Number,
                        TaxiId = o.TaxiOrderId,
                        CreatedDate = o.CreatedDate,
                        //Status = GetStatus(o.States),
                        //ActualTimes = GetActualTimes(o.States),
                        DriverInfo =
                            OrderToContractMappings.GetDriverInfo(OrderToContractMappings.GetCourierInfo(o.TaxiOrder)),
                        AutoInfo = OrderToContractMappings.GetAutoInfo(
                            OrderToContractMappings.GetCourierInfo(o.TaxiOrder)),
                        FeedBack = OrderToContractMappings.GetFeedBack(o.FeedBack),
                        //PlannedTime = o.States.Sum(s => s.PlannedTime),
                        MerchantOrderData = merchantOrders.FirstOrDefault(mo => mo.Id == o.MerchantOrderId)
                        //DeliveryInfo = new Contracts.DeliveryRequest {TaxiName = o.LinkToMobile}
                        //DeliveryInfo = GetDeliveryRequest(o.DeliveryRequest)
                    }).OrderByDescending(o => o.CreatedDate).ToListAsync();
                return orders;
            }

            public async Task<List<LightOrder>> GetMerchantOrders(OrderFilter filter, Guid userId)
            {
                await using var context = new GDEContext();
                var clientOrders = context.Set<ClientOrder>().AsQueryable();
                var orderWithTaxiQuery = context.Set<Repository.Model.Order>().Include(o => o.TaxiOrder).Where(o => o.TaxiOrder != null).Select(o => new {
                    o.Id,
                    o.Number,
                    o.IsDraft,
                    o.IsFastDelivery,
                    o.MerchantOrderId,
                    TaxiOrderId = o.TaxiOrderId,
                    TaxiId = o.TaxiOrder.TaxiId,
                    TaxiPrice = o.TaxiOrder.Price
                }).AsQueryable();
                var newOrderQuery = context.Set<Repository.Model.Order>().Where(o => o.TaxiOrderId == Guid.Empty).Select(o => new {
                    o.Id,
                    o.Number,
                    o.IsDraft,
                    o.IsFastDelivery,
                    o.MerchantOrderId,
                    TaxiOrderId = Guid.Empty,
                    TaxiId = Guid.Empty,
                    TaxiPrice = (double)0
                }).AsQueryable();
                var ordersQuery = orderWithTaxiQuery.Union(newOrderQuery);
                var merchantOrderQuery = context.Set<Repository.Model.MerchantOrder>().Select(mo => new { mo.Id, mo.MerchantId, mo.PointId }).AsQueryable();

                if (filter.StartDate.HasValue)
                {
                    clientOrders = clientOrders.Where(o => o.CreatedDate >= filter.StartDate.Value);
                }
                if (filter.EndDate.HasValue)
                {
                    clientOrders = clientOrders.Where(o => o.CreatedDate <= filter.EndDate.Value);
                }
                if (filter.Statuses != null && filter.Statuses.Any())
                    clientOrders = clientOrders.Where(o => filter.Statuses.Contains((OrderStatus)o.CurrentStatus));

                if (filter.Cities != null && filter.Cities.Any())
                {
                    var pointIds = context.Set<Point>()
                        .Where(o => filter.Cities.Contains(o.CityId)).Select(mo =>
                            mo.Id);
                    if (!(filter.Points != null && filter.Points.Any())) filter.Points = pointIds;
                }

                if (filter.Points != null && filter.Points.Any())
                {
                    merchantOrderQuery = merchantOrderQuery.Where(o => filter.Points.Contains(o.PointId)).AsQueryable();
                }
                var worker = await context.Set<Worker>().FirstOrDefaultAsync(w => w.UserId == userId);
                var user = await context.Set<User>().FirstOrDefaultAsync(u => u.Id == userId);
                if (worker != null)
                {
                    if (worker.PointId.HasValue)
                    {
                        merchantOrderQuery = merchantOrderQuery.Where(o => o.MerchantId == worker.MerchantId && o.PointId == worker.PointId)
                            .AsQueryable();
                    }
                    else if (user.Role != Role.Admin)
                    {
                        merchantOrderQuery = merchantOrderQuery.Where(mo => mo.MerchantId == worker.MerchantId)
                            .AsQueryable();
                    }
                }

                var orders = await clientOrders.Join(merchantOrderQuery, co => co.MerchantOrderId, mo => mo.Id, (clientOrder, merchantOrder) => new { merchantOrder, clientOrder })
                    .Join(ordersQuery, o => o.merchantOrder.Id, o => o.MerchantOrderId, (pair, order) => new { MerchantOrder = pair.merchantOrder, ClientOrder = pair.clientOrder, Order = order })
                    .Select(o => new LightOrder
                    {
                        Id = o.ClientOrder.Id,
                        ClientOrderNumber = o.ClientOrder.OrderNumber,
                        OrderNumber = o.Order.Number,
                        GiveCode = o.ClientOrder.GetOrderPin,
                        ReturnCode = o.ClientOrder.ReturnOrderPin,
                        TaxiPrice = o.Order.TaxiPrice,
                        Address = o.ClientOrder.Address,
                        ClientName = o.ClientOrder.ClientName,
                        ClientPhoneNumber = o.ClientOrder.PhoneNumber,
                        Date = o.ClientOrder.CreatedDate,
                        IntervalStart = o.ClientOrder.DateTimeIntervalStart,
                        IntervalEnd = o.ClientOrder.DateTimeIntervalEnd,
                        Status = (OrderStatus)o.ClientOrder.CurrentStatus,
                        //PointId = o.MerchantOrder.PointId,
                        CourierServiceId = o.Order.TaxiId,
                        TaxiOrderId = o.Order.TaxiOrderId,
                        IsDraft = o.Order.IsDraft,
                        NeedPayment = o.ClientOrder.PaymentSum > 0,
                        ////Price = deliveryOptions.FirstOrDefault(d =>
                        ////            d.CourierServiceId == o.TaxiOrder.TaxiId &&
                        ////            d.DeliveryRequestId == o.ClientOrder.DeliveryRequestId)
                        ////        .Price,
                        TotalPrice = o.ClientOrder.DeliveryCost ?? 0,
                        IsFastDelivery = o.Order.IsFastDelivery
                    })
                    .OrderByDescending(o => o.IntervalEnd)
                    .Take(50).ToListAsync();
                //var orders = (from o in clientOrders
                //    join merchantOrder in merchantOrders on o.MerchantOrderId equals merchantOrder.Id
                //    join order in ordersDb on o.MerchantOrderId equals order.MerchantOrderId
                //    join taxiOrderEl in taxiOrders on order.TaxiOrderId equals taxiOrderEl.Id into gj
                //    from taxiOrder in gj.DefaultIfEmpty()
                //    select new LightOrder
                //    {
                //        Id = o.Id,
                //        ClientOrderNumber = o.OrderNumber,
                //        OrderNumber = order.Number,
                //        Courier = new CourierInfo
                //            {Name = order.Courier?.User?.FirstName, Phone = order.Courier?.PhoneNumber},
                //        GiveCode = o.GetOrderPin,
                //        ReturnCode = o.ReturnOrderPin,
                //        TaxiPrice = taxiOrder?.Price ?? 0,
                //        Address = o.Address,
                //        ClientName = o.ClientName,
                //        ClientPhoneNumber = o.PhoneNumber,
                //        Date = o.CreatedDate,
                //        IntervalStart = o.DateTimeIntervalStart,
                //        IntervalEnd = o.DateTimeIntervalEnd,
                //        Status = (OrderStatus) o.CurrentStatus,
                //        PointId = merchantOrder.PointId,
                //        CourierServiceId = taxiOrder?.TaxiId ?? Guid.Empty,
                //        TaxiOrderId = taxiOrder?.Id ?? Guid.Empty,
                //        IsDraft = order.IsDraft,
                //        NeedPayment = o.PaymentSum > 0,
                //        Price = taxiOrder?.TaxiId != null
                //            ? deliveryOptions.FirstOrDefault(d =>
                //                    d.CourierServiceId == taxiOrder.TaxiId &&
                //                    d.DeliveryRequestId == o.DeliveryRequestId)?
                //                .Price ?? 0
                //            : 0,
                //        TotalPrice = o.DeliveryCost??0,
                //        IsFastDelivery = order.IsFastDelivery
                //    }).OrderByDescending(o => o.IntervalEnd).ToList();

                if (filter.Taxies != null && filter.Taxies.Any())
                {
                    orders = orders.Where(o => filter.Taxies.Contains(o.CourierServiceId)).ToList();
                }
                return orders;
            }

            //private async Task<OrderStatus> GetTaxiStatus(Guid taxiOrderId, Guid lightOrderCourierServiceId, GDEContext context)
            //{
            //    var taxi = await context.Set<CourierService>()
            //        .Where(p => p.Id == lightOrderCourierServiceId).FirstOrDefaultAsync();
            //    var type = Type.GetType($"GDE247.Services.Taxi.Concrete.{taxi.ServiceName},GDE247.Services.Taxi");
            //    var serviceToCall = Activator.CreateInstance(type) as ITaxiService;
            //    if (serviceToCall != null)
            //    {
            //        try
            //        {
            //            var taxiOrder = await serviceToCall.Get(taxiOrderId);
            //            return taxiOrder.CurrentStatus;

            //        }
            //        catch (Exception e)
            //        {
            //            Console.WriteLine(e);
            //            return OrderStatus.New;
            //        }

            //    }

            //    return OrderStatus.New;
            //}

            public async Task RecallOrderTaxi(Guid taxiOrderId)
            {
                await using var context = new GDEContext();
                var order = await context.Set<CourierServiceOrder>()
                    .Where(p => p.Id == taxiOrderId).FirstOrDefaultAsync();
                var orderDb = await context.Set<Repository.Model.Order>()
                    .Where(p => p.Id == order.OrderId).FirstOrDefaultAsync();
                var delivery = await context.Set<DeliveryRequest>().Include(d => d.Options)
                    .FirstOrDefaultAsync(d => d.Id == orderDb.DeliveryRequestId);
                var merchantOrder = JsonConvert.DeserializeObject<MerchantOrder>(delivery.MerchantOrderInfo);
                var taxi = await context.Set<CourierService>()
                    .Where(p => p.Id == order.TaxiId).FirstOrDefaultAsync();
                var type = Type.GetType($"GDE247.Services.Taxi.Concrete.{taxi.ServiceName},GDE247.Services.Taxi");
                if (Activator.CreateInstance(type ?? throw new InvalidOperationException()) is ITaxiService serviceToCall)
                {
                    var taxiOrder = await serviceToCall.CreateOrder(taxiOrderId, merchantOrder, "", "");
                    var taxiOrderDb = context.Set<CourierServiceOrder>().FirstOrDefault(o => o.Id == taxiOrderId);
                    if (taxiOrderDb != null) taxiOrderDb.AdditionalInfo = taxiOrder.OrderInfo;
                    await context.SaveChangesAsync();
                }
            }

            public async Task HandleOrderProblem(OrderProblemInfo problem)
            {
                await using var context = new GDEContext();
                var clientOrder = await context.Set<ClientOrder>().FirstOrDefaultAsync(o => o.Id == problem.Id);
                var order = await context.Set<Repository.Model.Order>()
                    .FirstOrDefaultAsync(o => o.MerchantOrderId == clientOrder.MerchantOrderId);
                order.Comment = problem.Comment;
                switch (problem.Action)
                {
                    case StatusReasonType.RecallAllWithTicket:
                        order.Status = Repository.Model.MainOrderStatus.New;
                        clientOrder.CurrentStatus = Repository.Model.OrderStatus.New;
                        await taxiHelper.RecallTaxis(order, true, true, context);
                        //TODO add ticket
                        //TODO add to blackList
                        break;
                    case StatusReasonType.CallAllOther:
                        await taxiHelper.RecallTaxis(order, false, true, context);
                        break;
                    case StatusReasonType.MerchantNotReady:
                        await taxiHelper.CancelAllTaxis(order.Id, context);
                        order.Status = Repository.Model.MainOrderStatus.New;
                        clientOrder.CurrentStatus = Repository.Model.OrderStatus.New;
                        if (problem.MoveMinutes.HasValue)
                        {
                            Observable
                                .Timer(TimeSpan.FromMinutes(problem.MoveMinutes.Value)).Subscribe(async x =>
                                {
                                    await taxiHelper.RecallTaxis(order, false, false, new GDEContext());
                                });
                        }

                        break;
                    case StatusReasonType.Return:
                        order.Status = Repository.Model.MainOrderStatus.Return;
                        clientOrder.CurrentStatus = Repository.Model.OrderStatus.Returning;
                        break;
                    case StatusReasonType.CancelOrder:
                        await CancelOrder(problem.Id);
                        break;
                    case StatusReasonType.NoAction:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                await context.SaveChangesAsync();

            }

            public async Task<OrderInfo> GetOrderDraft(Guid clientOrderId)
            {
                await using var context = new GDEContext();
                var clientOrder = await context.Set<ClientOrder>().FirstOrDefaultAsync(o => o.Id == clientOrderId);
                var merchantOrder = await context.Set<Repository.Model.MerchantOrder>().Include(o => o.ClientOrders)
                    .FirstOrDefaultAsync(p => p.Id == clientOrder.MerchantOrderId);
                var order = await context.Set<Repository.Model.Order>()
                    .Select(o => new { o.Id, o.MerchantOrderId, o.IsDraft, o.DeliveryRequestId })
                    .FirstOrDefaultAsync(o => o.MerchantOrderId == merchantOrder.Id);

                return new OrderInfo
                {
                    Id = order.Id,
                    Orders = merchantOrder.ClientOrders.Select(co => new ClientOrderInfo
                    {
                        OrderNumber = co.OrderNumber,
                        Recipient = new ClientInfo
                        {
                            FioRaw = co.ClientName,
                            Address = new AddressFullInfo
                            {
                                Latitude = co.Latitude,
                                Longitude = co.Longitude,
                                Address = co.Address
                            },
                            Phone = co.PhoneNumber
                        },
                        NeedTermoPack = false,//todo
                        PaymentSum = co.PaymentSum,
                        OrderPrice = co.Summa,
                        SourceId = co.SourceId,
                        Places = new List<DeliveryPlace>() { new DeliveryPlace() },
                        TimeFrom = co.DateTimeIntervalStart.ToString(),
                        TimeTo = co.DateTimeIntervalEnd.ToString()
                    }).ToList(),
                    DeliveryRequestId = order.DeliveryRequestId,
                    IsDraft = order.IsDraft,
                    UseOptimization = merchantOrder.ClientOrders.FirstOrDefault()?.UseOptimization ?? false,
                    Sender = new MerchantInfo
                    {
                        MerchantId = merchantOrder.MerchantId,
                        PointId = merchantOrder.PointId
                    }
                };
            }

            public async Task CancelOrder(Guid id)
            {
                await using var context = new GDEContext();
                var clientOrderInit = await context.Set<ClientOrder>().FirstOrDefaultAsync(o => o.Id == id);
                var order = await context.Set<Repository.Model.Order>()
                    .FirstOrDefaultAsync(o => o.MerchantOrderId == clientOrderInit.MerchantOrderId);

                order.Status = Repository.Model.MainOrderStatus.Cancelled;
                var clientOrders = context.Set<ClientOrder>().Include(o => o.States)
                    .Where(co => co.MerchantOrderId == clientOrderInit.MerchantOrderId).ToList();
                foreach (var clientOrder in clientOrders)
                {
                    clientOrder.CurrentStatus = Repository.Model.OrderStatus.Cancelled;
                }
                if (order.PaymentId > 0)
                {
                    await PaymentHelper.Cancel(1000, order.PaymentId);
                }
                order.CancelTime = DateTimeOffset.UtcNow;
                await taxiHelper.CancelAllTaxis(order.Id, context);
                await context.SaveChangesAsync();
            }


            public async Task<LightOrder> GetLightOrder(Guid orderId)
            {
                using (var context = new GDEContext())
                {
                    var order = await context.Set<Repository.Model.Order>().Select(o => new { o.Id, o.MerchantOrderId })
                        .FirstOrDefaultAsync(o => o.Id == orderId);
                    var merchantOrder = await context.Set<Repository.Model.MerchantOrder>().Include(m => m.Merchant)
                        .FirstOrDefaultAsync(p => p.Id == order.MerchantOrderId);
                    var product = await context.Set<MerchantProduct>()
                        .FirstOrDefaultAsync(p => p.Id == merchantOrder.ProductId);
                    return new LightOrder
                    {
                        Id = orderId,
                        //OrderNumber = merchantOrder.OrderNumber,
                        //ClientName = merchantOrder.ClientName,
                        //PhoneNumber = merchantOrder.PhoneNumber,
                        MerchantName = merchantOrder.Merchant.Name,
                        ProductName = product.Name
                    };
                }
            }

            public async Task<OrderDetails> GetOrderDetails(Guid clientOrderId)
            {
                await using var context = new GDEContext();
                var clientOrder = await context.Set<ClientOrder>().FirstOrDefaultAsync(o => o.Id == clientOrderId);
                var orderStates = await context.Set<OrderState>().Where(o => o.OrderId == clientOrderId).ToListAsync();
                var assignedState = orderStates.First(o => o.OrderId == clientOrderId && o.Status == Repository.Model.OrderStatus.DriverAssigned);
                var arrivedState = orderStates.First(o => o.OrderId == clientOrderId && o.Status == Repository.Model.OrderStatus.DriverArrived);
                var ridingState = orderStates.First(o => o.OrderId == clientOrderId && o.Status == Repository.Model.OrderStatus.Riding);
                var deliveredState = orderStates.First(o => o.OrderId == clientOrderId && o.Status == Repository.Model.OrderStatus.Delivered);
                var completedState = orderStates.FirstOrDefault(o => o.OrderId == clientOrderId && o.Status == Repository.Model.OrderStatus.Completed);
                var merchantOrder = await context.Set<Repository.Model.MerchantOrder>().Include(o => o.ClientOrders)
                    .FirstOrDefaultAsync(p => p.Id == clientOrder.MerchantOrderId);
                var order = await context.Set<Repository.Model.Order>()
                    .Select(o => new { o.Id, o.TaxiOrderId, o.MerchantOrderId, o.Number, o.Status, o.CreatedDate, o.Comment, o.IsFastDelivery, o.DeliveryRequestId, o.CallTaxiInMinutes, o.CancelTime })
                    .FirstOrDefaultAsync(o => o.MerchantOrderId == merchantOrder.Id);
                var taxiOrder = await context.Set<CourierServiceOrder>()
                    .FirstOrDefaultAsync(o => o.Id == order.TaxiOrderId);
                var point = await context.Set<Point>().FirstOrDefaultAsync(p => p.Id == merchantOrder.PointId);
                CourierService taxi = null;
                if (taxiOrder != null)
                    taxi = await context.Set<CourierService>()
                        .Where(p => p.Id == taxiOrder.TaxiId).FirstOrDefaultAsync();

                var deliveryRequestOptions = await context.Set<DeliveryRequestOption>().Where(dop => dop.DeliveryRequestId == order.DeliveryRequestId).ToListAsync();
                var taxiOrders = await context.Set<CourierServiceOrder>().Where(o => o.OrderId == order.Id).ToListAsync();
                var taxis = await context.Set<CourierService>().ToListAsync();

                var times = from deliveryRequestOption in deliveryRequestOptions
                            join t in taxis on deliveryRequestOption.CourierServiceId equals t.Id
                            join to in taxiOrders on deliveryRequestOption.CourierServiceId equals to.TaxiId into ps
                            from to in ps.DefaultIfEmpty()
                            select new DeliverOptionTime { CourierService = t.Name, Price = deliveryRequestOption.Price, TimeCalled = to?.CallDateTime };
                //        var q =
                //from c in categories
                //join p in products on c.Category equals p.Category into ps
                //from p in ps.DefaultIfEmpty()
                //select new { Category = c, ProductName = p == null ? "(No products)" : p.ProductName };
                return new OrderDetails
                {
                    Id = clientOrderId,
                    OrderNumber = order.Number,
                    CreatedDateTime = clientOrder.CreatedDate.UtcDateTime,
                    Status = (MainOrderStatus)order.Status,
                    Comment = order.Comment,
                    Eta = assignedState.ActualTime?.AddMinutes(arrivedState.PlannedTime).UtcDateTime,
                    ArrivedTime = arrivedState.ActualTime?.UtcDateTime,
                    AssignedTime = assignedState.ActualTime?.UtcDateTime,
                    StartRidingTime = ridingState.ActualTime?.UtcDateTime,
                    ClientOrders = merchantOrder.ClientOrders.Select(co => new Contracts.ClientOrder
                    {
                        OrderNumber = co.OrderNumber,
                        Client = new ClientInfo
                        {
                            FioRaw = co.ClientName,
                            Address = new AddressFullInfo
                            {
                                Latitude = co.Latitude,
                                Longitude = co.Longitude,
                                Address = co.Address
                            },
                            Phone = co.PhoneNumber,
                            Comment = co.Comment
                        },

                        CurrentStatus = (OrderStatus)co.CurrentStatus,
                        OrderIndex = co.OrderIndex,
                        PaymentSum = co.PaymentSum,
                        DeliveryCost = co.DeliveryCost,
                        OrderPrice = co.Summa,
                        Eta = ridingState.ActualTime?.AddMinutes(deliveredState.PlannedTime).UtcDateTime ?? arrivedState.ActualTime?.AddMinutes(ridingState.PlannedTime).AddMinutes(deliveredState.PlannedTime).UtcDateTime,
                        DeliveryTime = completedState?.ActualTime ?? deliveredState.ActualTime,
                        StartDateTime = co.DateTimeIntervalStart,
                        EndDateTime = co.DateTimeIntervalEnd
                    }).ToList(),
                    StartPointLatitude = point.Latitude,
                    StartPointLongitude = point.Longitude,
                    TaxiOrderId = taxiOrder?.Id ?? Guid.Empty,
                    TaxiId = taxi?.Id ?? Guid.Empty,
                    IsFastDelivery = order.IsFastDelivery,
                    TaxiName = taxi != null ? taxi.Name : string.Empty,
                    Options = times.ToList(),
                    CallInTime = order.CallTaxiInMinutes,
                    CancelledTime = order.CancelTime?.UtcDateTime,
                };
            }

            public async Task<OrderDeliveryInfo> CreateExternalOrder(OrderInfo order)
            {
                try
                {
                    await using var context = new GDEContext();
                    var merchant = await context.Set<Merchant>().Where(m => m.Id == order.Sender.MerchantId)
                        .FirstOrDefaultAsync();
                    MerchantOrder merchantOrder;
                    DeliveryRequest delivery = null;
                    if (!order.IsDraft)
                    {
                        if (order.DeliveryRequestId == null) throw new Exception("delivery request is empty");
                        delivery = await GetDeliveryRequest(order.DeliveryRequestId.Value);
                        if (delivery == null) throw new Exception("delivery service not found");
                        var duplicateOrder = await context.Set<Repository.Model.Order>().FirstOrDefaultAsync(o => o.DeliveryRequestId == order.DeliveryRequestId);
                        if (duplicateOrder != null) return null;
                        delivery.Options = delivery.Options.Where(o =>
                                merchant.MaxDeliveryPriceInPenny == null ||
                                o.Price <= merchant.MaxDeliveryPriceInPenny / 100)
                            .ToList();
                        if (delivery.Options.Count == 0)
                            throw new Exception(
                                $"Цена доставки превышает заданный лимит! лимит: {merchant.MaxDeliveryPriceInPenny / 100}");
                        merchantOrder = JsonConvert.DeserializeObject<MerchantOrder>(delivery.MerchantOrderInfo);
                    }
                    else
                    {
                        merchantOrder = await convertOrderHelper.ConvertMerchantOrder(order, context, null);
                    }
                    var orderId = order.Id ?? Guid.NewGuid();
                    var link = await mobstedApiHelper.CreateObject(
                        new User { Id = orderId, Username = orderId.ToString() }, orderId,
                        MobstedApiHelper.CourierApplicationId, null, null); //create mobile for order not for courier
                    if (order.CourierId.HasValue)
                    {
                        var courier = await context.Set<Courier>().Include(c => c.User).FirstOrDefaultAsync(c => c.Id == order.CourierId.Value);
                        await mobstedApiHelper.CreateObject(
                        courier.User, orderId,
                        MobstedApiHelper.OwnCourierApplicationId, null, null);
                    }
                    context.Database.BeginTransaction();
                    //context.Set<ClientOrder>().RemoveRange(connectedClientOrders);
                    var merchantOrderDb = SaveMerchantOrderToDb(context, merchantOrder, order.DeliveryRequestId);
                    // var verificationOrderId = await SaveVerificationOrderToDb(context, merchantOrder);
                    if (!order.IsDraft)
                    {
                        var time = order.Orders.FirstOrDefault()?.Time - 10;
                        if (time > 0)
                        {
                            Observable
                                .Timer(TimeSpan.FromMinutes((double)time)).Subscribe(async x =>
                                {
                                    await taxiHelper.CreateTaxiOrders(delivery, merchantOrder, link, orderId, new GDEContext());
                                });
                        }
                        else
                        {
                            await taxiHelper.CreateTaxiOrders(delivery, merchantOrder, link, orderId, context);
                        }
                    }

                    var orderDb = SaveOrderToDb(context, orderId, merchantOrderDb.Id, delivery, null, null, order.Orders.FirstOrDefault()?.Time, order.IsFastDelivery);
                    LogWriter.LogWrite("order saved to db");

                    foreach (var clientOrder in merchantOrderDb.ClientOrders)
                    {
                        var clientDb = await GetOrCreateClient(clientOrder, context);
                        clientOrder.LinkToMobile = await mobstedApiHelper.CreateObject(clientDb.User, clientOrder.Id,
                            MobstedApiHelper.ClientApplicationId, null, null);
                    }

                    orderDb.IsDraft = order.IsDraft;
                    orderDb.LinkToMobile = link;
                    if (delivery?.Options.Count == 1)
                        orderDb.TaxiOrderId =
                            context.Set<CourierServiceOrder>().FirstOrDefault(o => o.OrderId == orderDb.Id)?.Id ??
                            Guid.Empty;
                    context.Database.CommitTransaction();
                    context.SaveChanges();

                    //if (order.SendSMSToClient)
                    //foreach (var clientOrder in merchantOrderDb.ClientOrders)
                    //{
                    //    //TODO ensure was sent later
                    //    clientOrder.SmsId = await smsHelper.SendSms(clientOrder.PhoneNumber,
                    //        smsHelper.GenerateClientSmsText(clientOrder.OrderNumber, merchantOrderDb.Merchant.Name,
                    //            clientOrder.LinkToMobile));
                    //    clientOrder.SmsSendTimeStamp = DateTimeOffset.UtcNow;
                    //}

                    return new OrderDeliveryInfo
                    {
                        Id = merchantOrderDb.ClientOrders.First().Id,
                        OrderId = orderDb.Id,
                        Status = MainOrderStatus.New.ToString()
                    };
                }
                catch (Exception e)
                {
                    LogWriter.LogWrite($"taxi order error {e}");
                    throw;
                }
            }

            public Task<OrderDeliveryInfo> CreateSingleExternalOrder(OnePointOrder order, Guid merchantId)
            {
                throw new NotImplementedException();
            }

            private async Task<Client> GetOrCreateClient(ClientOrder clientOrder, GDEContext context)
            {
                var clientDb = await context.Set<Client>().Include(c => c.User)
                    .FirstOrDefaultAsync(c => c.PhoneNumber == clientOrder.PhoneNumber);
                if (clientDb == null)
                {
                    byte[] passwordHash, passwordSalt;
                    PasswordHelper.CreatePasswordHash(clientOrder.PhoneNumber, out passwordHash, out passwordSalt);
                    var userDb = new User
                    {
                        Id = Guid.NewGuid(),
                        Username = clientOrder.PhoneNumber,
                        LastName = clientOrder.ClientName,
                        Role = Role.Client,
                        Password = passwordHash,
                        Token = passwordSalt
                    };
                    await Task.Run(() => context.Set<User>().Add(userDb)); // addAsync doesn't work
                    clientDb = new Client
                    {
                        Id = Guid.NewGuid(),
                        PhoneNumber = clientOrder.PhoneNumber,
                        User = userDb,
                        UserId = userDb.Id,
                        PhoneExtraNumber = clientOrder.PhoneExtraNumber,
                        MainAddress = clientOrder.Address
                    };
                    await Task.Run(() => context.Set<Client>().Add(clientDb));
                }

                return clientDb;
            }

            private async Task<Courier> GetCourier(CourierInfo courier, GDEContext context)
            {
                var courierDb = await context.Set<Courier>().FirstOrDefaultAsync(c => c.PhoneNumber == courier.Phone);
                if (courierDb == null)
                {
                    //var service = await context.Set<CourierService>()
                    //    .FirstOrDefaultAsync(s => s.ExternalId == courier.ServiceCode);
                    byte[] passwordHash, passwordSalt;
                    PasswordHelper.CreatePasswordHash(courier.Phone, out passwordHash, out passwordSalt);
                    var userDb = new User
                    {
                        Id = Guid.NewGuid(),
                        Username = courier.Phone,
                        LastName = courier.Name,
                        Role = Role.Courier
                    };
                    userDb.Password = passwordHash;
                    userDb.Token = passwordSalt;
                    await Task.Run(() => context.Set<User>().Add(userDb)); // addAsync doesn't work
                    courierDb = new Courier
                    {
                        Id = Guid.NewGuid(),
                        PhoneNumber = courier.Phone,
                        User = userDb,
                        UserId = userDb.Id,
                        //CourierServiceId = service?.Id
                    };
                    await Task.Run(() => context.Set<Courier>().Add(courierDb));
                }

                return courierDb;
            }

            private async Task<Guid?> SaveVerificationOrderToDb(GDEContext context, MerchantOrder merchantOrder)
            {
                if (merchantOrder.NeedVerification)
                {
                    var verificationOrderId = Guid.NewGuid();
                    var verificationOrder = new VerificationOrder
                    {
                        Id = verificationOrderId,
                        Status = VerificationStatus.New,
                        Comment = merchantOrder.Comment
                    };
                    await context.Set<VerificationOrder>().AddAsync(verificationOrder);
                    var documents = await context.Set<ProductDocument>()
                        .Where(p => p.ProductId == merchantOrder.MerchantProductId).ToListAsync();
                    foreach (var productDocument in documents)
                        await context.Set<VerificationDocument>().AddAsync(
                            new VerificationDocument
                            {
                                VerificationOrderId = verificationOrderId,
                                DocumentId = productDocument.DocumentId,
                                ProductId = productDocument.ProductId,
                                Status = DocumentVerificationStatus.New
                            });

                    var product = await context.Set<MerchantProduct>()
                        .FirstOrDefaultAsync(p => p.Id == merchantOrder.MerchantProductId);
                    if (product.QuestionnaireId.HasValue)
                    {
                        var questions = await context.Set<Questionnaire>().Include(q => q.Questions)
                            .FirstOrDefaultAsync(q => q.Id == product.QuestionnaireId);
                        await context.Set<QuestionnaireAnswer>().AddAsync(
                            new QuestionnaireAnswer
                            {
                                QuestionnaireId = product.QuestionnaireId.Value,
                                VerificationOrderId = verificationOrderId,
                                Answers = questions.Questions.Select(a => new QuestionAnswer
                                {
                                    QuestionnaireId = product.QuestionnaireId.Value,
                                    VerificationOrderId = verificationOrderId,
                                    QuestionId = a.QuestionId
                                }).ToList()
                            }
                        );
                        verificationOrder.QuestionnaireId = product.QuestionnaireId.Value;
                    }

                    return verificationOrderId;
                }

                return null;
            }

            private Repository.Model.Order SaveOrderToDb(GDEContext context, Guid orderId,
                Guid merchantOrderId, DeliveryRequest delivery, Guid? verificationOrderId,
                Guid? courierId, int? time, bool isFastDelivery)
            {
                var order = context.Set<Repository.Model.Order>().FirstOrDefault(o => o.Id == orderId) ?? new Repository.Model.Order();
                var orderNumber = DateTime.Now.ToString("yyyyMMddHHmmss");
                order.MerchantOrderId = merchantOrderId;
                order.DeliveryRequestId = delivery?.Id;
                order.VerificationOrderId = verificationOrderId;
                order.CourierId = courierId;
                if (order.Id == Guid.Empty)
                {
                    order.Id = orderId;
                    order.Number = orderNumber;
                    order.CreatedDate = DateTimeOffset.UtcNow;
                    order.CallTaxiInMinutes = time ?? 0;
                    order.IsFastDelivery = isFastDelivery;
                    context.Set<Repository.Model.Order>().Add(order);
                }

                return order;
            }

            private async Task SaveTaxiOrderToDb(GDEContext context, CourierServiceOrder taxiOrder)
            {
                await Task.Run(() =>
                {
                    context.Set<CourierServiceOrder>().Add(taxiOrder);
                    context.SaveChanges();
                });
            }

            private async Task<DeliveryRequest> GetDeliveryRequest(Guid deliveryRequestId)
            {
                using (var context = new GDEContext())
                {
                    return await context.Set<DeliveryRequest>().Include(d => d.Options)
                        .FirstOrDefaultAsync(d => d.Id == deliveryRequestId);
                }
            }

            private Repository.Model.MerchantOrder SaveMerchantOrderToDb(GDEContext context, MerchantOrder merchantOrder,
                Guid? deliveryRequestId)
            {
                var point = context.Set<Point>().FirstOrDefault(p => p.Id == merchantOrder.PointId);
                var merchantOrderDb =
                    context.Set<Repository.Model.MerchantOrder>().Include(c => c.ClientOrders).FirstOrDefault(mo => mo.Id == merchantOrder.Id) ??
                    new Repository.Model.MerchantOrder();
                merchantOrderDb.MerchantId = merchantOrder.MerchantId;
                merchantOrderDb.PointId = merchantOrder.PointId;
                merchantOrderDb.ProductId = merchantOrder.MerchantProductId;
                if (merchantOrderDb.Id == Guid.Empty)
                {
                    context.Set<Repository.Model.MerchantOrder>().Add(merchantOrderDb);
                }

                merchantOrderDb.Id = merchantOrder.Id;
                var clientOrders = merchantOrder.ClientOrders.Select(co =>
                {
                    var order = context.Set<ClientOrder>().FirstOrDefault(mo => mo.Id == co.Id) ?? new ClientOrder();
                    var orderId = order.Id != Guid.Empty ? order.Id : Guid.Empty;
                    order.Id = orderId;
                    order.CreatedDate = DateTimeOffset.UtcNow;
                    order.MerchantOrderId = merchantOrder.Id;
                    order.Address = string.IsNullOrEmpty(co.Client.Address.Street)
                        ? co.Client.Address.Address
                        : $"{co.Client.Address.Street}, д{co.Client.Address.House}, кв{co.Client.Address.Apartment}, под{co.Client.Address.Entrance}, домоф{co.Client.Address.Intercom}, эт{co.Client.Address.Floor}";
                    order.Longitude = co.Client.Address.Longitude;
                    order.Latitude = co.Client.Address.Latitude;
                    order.PhoneNumber = co.Client.Phone;
                    order.OrderNumber = co.OrderNumber;
                    order.OrderType = (OrderProductType)merchantOrder.OrderType;
                    order.DeliveryType = (DeliveryType)co.DeliveryType;
                    order.ClientName = co.Client.FioRaw;
                    order.SourceId = co.SourceId;
                    order.GetOrderPin = pinHelper.GenerateRandomNo();
                    order.GiveOrderPin =
                        pinHelper.GenerateRandomNo(); //await pinHelper.GetGivePin(delivery.CourierServiceId.Value);
                    order.Paid = co.Paid;
                    order.Summa = co.OrderPrice ?? 0;
                    order.ShoppingList = JsonConvert.SerializeObject(co.Items);
                    order.Weight = co.Weight;
                    order.PaymentSum = co.PaymentSum;
                    order.PrepaidValue = co.PrepaidValue;
                    order.Comment = co.Client.Comment;
                    order.DeliveryCost = co.DeliveryCost;
                    order.DeliveryRequestId = deliveryRequestId;
                    order.DateTimeIntervalStart = co.StartDateTime?.ToUniversalTime() ?? DateTimeOffset.UtcNow.AddMinutes(15);
                    order.DateTimeIntervalEnd = co.EndDateTime?.ToUniversalTime() ?? DateTimeOffset.UtcNow.AddMinutes(75);
                    order.PlacesCount = co.Places.Count;
                    return order;
                }).ToList();
                var existClientOrders = clientOrders.Where(co => co.Id != Guid.Empty);
                foreach (var existClientOrder in existClientOrders)
                {
                    var ridingEstimatedTimeInSec = existClientOrder.Latitude != 0 ? GoogleApiHelper.GetTime(point.Latitude, existClientOrder.Latitude, point.Longitude,
                        existClientOrder.Longitude) : 0;
                    if (ridingEstimatedTimeInSec > 0)
                    {
                        var ridingState = context.Set<OrderState>().FirstOrDefault(s =>
                            s.OrderId == existClientOrder.Id && s.Status == Repository.Model.OrderStatus.Delivered);
                        if (ridingState != null) ridingState.PlannedTime = (int)(ridingEstimatedTimeInSec / 60);
                    }

                }
                var newClientOrders = clientOrders.Where(co => co.Id == Guid.Empty);
                foreach (var newClientOrder in newClientOrders)
                {
                    newClientOrder.Id = merchantOrder.ClientOrders.FirstOrDefault(c => c.OrderNumber == newClientOrder.OrderNumber)?.Id ?? Guid.NewGuid();
                    newClientOrder.States = CreateStates(newClientOrder.Id, point, newClientOrder, context);
                    context.Set<ClientOrder>().Add(newClientOrder);
                }


                merchantOrderDb.ClientOrders ??= clientOrders;

                LogWriter.LogWrite("merchant order saved to db");
                return merchantOrderDb;
            }

            public async Task ClientGotOrder(Guid orderId)
            {
                using (var context = new GDEContext())
                {
                    var orderDb = context.Set<Repository.Model.Order>().Include(o => o.TaxiOrder)
                        .Include(o => o.MerchantOrder)
                        .FirstOrDefault(order => order.Id == orderId);
                    if (orderDb != null)
                    {
                        //orderDb.MarkedAsDelivered = true;
                        context.SaveChanges();
                        var point = context.Set<Point>().FirstOrDefault(p => p.Id == orderDb.MerchantOrder.PointId);
                        var exTaxiOrder = new TaxiOrder
                        {
                            Id = orderDb.TaxiOrderId,
                            OrderInfo = orderDb.TaxiOrder.AdditionalInfo
                        };
                        if (point != null)
                            await TaxiServiceHelper.GetTaxiService("Maxim")
                                .UpdateOrderComment(exTaxiOrder, "ЗАКАЗ ПОЛУЧЕН");
                    }
                }
            }


            private Collection<OrderState> CreateStates(Guid orderId, Point point, ClientOrder clientOrder, GDEContext context)
            {
                var plannedTimes = CalculatePlannedTimes(point, clientOrder, context);
                var result = new Collection<OrderState>();

                foreach (var plannedTime in plannedTimes)
                    result.Add(new OrderState
                    {
                        OrderId = orderId,
                        PlannedTime = plannedTime.Value,
                        Status = (Repository.Model.OrderStatus)plannedTime.Key
                    });

                var newStatus = result.FirstOrDefault(s => s.Status == Repository.Model.OrderStatus.New);
                if (newStatus == null)
                    result.Add(new OrderState
                    {
                        OrderId = orderId,
                        PlannedTime = 0,
                        Status = Repository.Model.OrderStatus.New,
                        ActualTime = DateTimeOffset.UtcNow.UtcDateTime,
                    });
                else
                    newStatus.ActualTime = DateTimeOffset.UtcNow.UtcDateTime;

                return result;
            }

            private Dictionary<OrderStatus, int> CalculatePlannedTimes(Point point, ClientOrder clientOrder, GDEContext context) //TODO expand for route
            {
                //var averageDriverAssignedTime = context.Set<OrderState>().Average(os => os.ActualTime)
                var orderStates = context.Set<OrderState>().Where(os => os.ActualTime != null).AsEnumerable()
                    .GroupBy(os => os.OrderId).ToDictionary(os => os.Key);
                var averageDriverArrivedTime = orderStates
                    .Average(os =>
                        (os.Value.FirstOrDefault(o => o.Status == Repository.Model.OrderStatus.DriverArrived)?.ActualTime -
                         os.Value.FirstOrDefault(o => o.Status == Repository.Model.OrderStatus.DriverAssigned)?.ActualTime)?
                        .Minutes);
                var averagePickUpTime = orderStates
                    .Average(os =>
                        (os.Value.FirstOrDefault(o => o.Status == Repository.Model.OrderStatus.Riding)?.ActualTime -
                         os.Value.FirstOrDefault(o => o.Status == Repository.Model.OrderStatus.DriverArrived)?.ActualTime)?
                        .Minutes);
                var ridingEstimatedTimeInSec = clientOrder.Latitude != 0 ? GoogleApiHelper.GetTime(point.Latitude, clientOrder.Latitude, point.Longitude,
                    clientOrder.Longitude) : 0;
                return new Dictionary<OrderStatus, int>
            {
                {OrderStatus.DriverAssigned, 3},
                {OrderStatus.DriverArrived, (int?) averageDriverArrivedTime ?? 10},
                {OrderStatus.Riding, (int?) averagePickUpTime ?? 10}, //point?.WaitingTime / 60 ??
                {OrderStatus.Delivered, (int) (ridingEstimatedTimeInSec/60)}
            };
            }

            private string GetStatus(Collection<OrderState> oStates)
            {
                var actualState = oStates.OrderByDescending(s => s.ActualTime)
                    .FirstOrDefault(s => s.ActualTime != null);
                if (actualState != null) return actualState.Status.ToString();

                return OrderStatus.New.ToString();
            }

            private Dictionary<OrderStatus, int> GetActualTimes(Collection<OrderState> oStates)
            {
                var result = new Dictionary<OrderStatus, int>();
                if (oStates != null)
                {
                    var passedStates = oStates.Where(s => s.ActualTime != null).ToList();
                    for (var i = 1; i < passedStates.Count; i++)
                    {
                        var orderState = passedStates[i];
                        if (orderState.ActualTime != null)
                            result.Add((OrderStatus)orderState.Status,
                                (int)(orderState.ActualTime - passedStates[i - 1].ActualTime).Value.TotalMinutes);
                    }
                }

                return result;
            }

            public async Task SaveFeedBack(Guid orderId, string comment, Rate? rating)
            {
                using (var context = new GDEContext())
                {
                    var orderDb = await context.Set<Repository.Model.Order>().Include(o => o.FeedBack)
                        .FirstOrDefaultAsync(o => o.Id == orderId);
                    if (orderDb != null)
                    {
                        orderDb.FeedBack = orderDb.FeedBack ?? new FeedBack();
                        if (comment != null)
                            orderDb.FeedBack.Comment = comment;
                        if (rating != null)
                            orderDb.FeedBack.Rating = (Repository.Model.Rate)rating;
                    }

                    await context.SaveChangesAsync();
                }
            }
        }

    internal class CourierServiceOrder
    {
        public Guid Id { get; set; }
        public Guid? TaxiId { get; set; }
        public object AdditionalInfo { get; set; }
    }

    internal class GDEContext:DBContext
    {
        public GDEContext()
        {
        }
    }

    public class CourierInfo
    {
    }

    public class MerchantOrder
    {
    }

    internal class ConvertOrderHelper
    {
    }

    internal class PinHelper
    {
    }

    internal class TaxiHelper
    {
    }

    internal class MobstedApiHelper
    {
        public static object CourierApplicationId { get; internal set; }

        internal Task<string> CreateObject(object user, object id1, object courierApplicationId, object id2, Guid? verificationOrderId)
        {
            throw new NotImplementedException();
        }
    }
}

