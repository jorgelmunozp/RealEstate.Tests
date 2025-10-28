using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.Owner.Dto;
using RealEstate.API.Modules.Owner.Interface;
using RealEstate.API.Modules.Owner.Model;
using RealEstate.API.Modules.Property.Dto;
using RealEstate.API.Modules.Property.Model;
using RealEstate.API.Modules.Property.Service;
using RealEstate.API.Modules.PropertyImage.Dto;
using RealEstate.API.Modules.PropertyImage.Interface;
using RealEstate.API.Modules.PropertyImage.Model;
using RealEstate.API.Modules.PropertyTrace.Dto;
using RealEstate.API.Modules.PropertyTrace.Interface;
using RealEstate.API.Modules.PropertyTrace.Model;

namespace RealEstate.Tests.Modules.Property.Service
{
    [TestFixture]
    public class PropertyServiceTests
    {
        Mock<IMongoDatabase> _db = null!;
        Mock<IMongoCollection<PropertyModel>> _props = null!;
        Mock<IMongoCollection<OwnerModel>> _owners = null!;
        Mock<IMongoCollection<PropertyImageModel>> _images = null!;
        Mock<IMongoCollection<PropertyTraceModel>> _traces = null!;
        Mock<IValidator<PropertyDto>> _validator = null!;
        IMemoryCache _cache = null!;
        IConfiguration _cfg = null!;
        Mock<IOwnerService> _ownerSvc = null!;
        Mock<IPropertyImageService> _imgSvc = null!;
        Mock<IPropertyTraceService> _traceSvc = null!;
        PropertyService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _db = new Mock<IMongoDatabase>();
            _props = new Mock<IMongoCollection<PropertyModel>>();
            _owners = new Mock<IMongoCollection<OwnerModel>>();
            _images = new Mock<IMongoCollection<PropertyImageModel>>();
            _traces = new Mock<IMongoCollection<PropertyTraceModel>>();

            _cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["MONGO_COLLECTION_PROPERTY"]="properties",
                ["MONGO_COLLECTION_OWNER"]="owners",
                ["MONGO_COLLECTION_PROPERTYIMAGE"]="images",
                ["MONGO_COLLECTION_PROPERTYTRACE"]="traces",
                ["CACHE_TTL_MINUTES"]="1"
            }).Build();

            _db.Setup(d=>d.GetCollection<PropertyModel>("properties",null)).Returns(_props.Object);
            _db.Setup(d=>d.GetCollection<OwnerModel>("owners",null)).Returns(_owners.Object);
            _db.Setup(d=>d.GetCollection<PropertyImageModel>("images",null)).Returns(_images.Object);
            _db.Setup(d=>d.GetCollection<PropertyTraceModel>("traces",null)).Returns(_traces.Object);

            _validator = new Mock<IValidator<PropertyDto>>();
            _validator.Setup(v=>v.ValidateAsync(It.IsAny<PropertyDto>(),It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());

            _cache = new MemoryCache(new MemoryCacheOptions());
            _ownerSvc = new Mock<IOwnerService>();
            _imgSvc = new Mock<IPropertyImageService>();
            _traceSvc = new Mock<IPropertyTraceService>();

            _sut = new PropertyService(_db.Object,_validator.Object,_cfg,_cache,_ownerSvc.Object,_imgSvc.Object,_traceSvc.Object);
        }

        // ---------- Helpers (mockear Find* sin tocar métodos de extensión) ----------
        static Mock<IAsyncCursor<T>> Cursor<T>(IReadOnlyList<T> items) where T:class
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count>0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(items.Count>0).ReturnsAsync(false);
            cursor.SetupGet(c => c.Current).Returns(items);
            return cursor;
        }
        static void SetupFind<T>(Mock<IMongoCollection<T>> col, IReadOnlyList<T> items) where T:class
        {
            var cur = Cursor(items);
            col.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<FindOptions<T,T>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(cur.Object);
            col.Setup(c => c.FindSync(It.IsAny<FilterDefinition<T>>(), It.IsAny<FindOptions<T,T>>(), It.IsAny<CancellationToken>()))
               .Returns(cur.Object);
        }
        static void SetupFindOne<T>(Mock<IMongoCollection<T>> col, T? one) where T:class
            => SetupFind(col, one!=null ? new List<T>{one} : new List<T>());
        static void SetupFindMany<T>(Mock<IMongoCollection<T>> col, IEnumerable<T> many) where T:class
            => SetupFind(col, many.ToList());
        static DeleteResult Del(long n)
        {
            var m=new Mock<DeleteResult>();
            m.SetupGet(x=>x.IsAcknowledged).Returns(true);
            m.SetupGet(x=>x.DeletedCount).Returns(n);
            return m.Object;
        }

        // =================== TESTS ===================

        [Test]
        public async Task GetByIdAsync_ShouldReturn404_WhenNotFound()
        {
            SetupFindOne(_props, (PropertyModel?)null);
            var res = await _sut.GetByIdAsync("nope");
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
            res.Message.Should().Contain("no encontrada");
        }

        [Test]
        public async Task GetByIdAsync_ShouldReturnDto_WithOwnerImageAndTraces()
        {
            var prop=new PropertyModel{ Id="p1", Name="Casa", Address="A", Price=1000L, Year=2020, CodeInternal=10, IdOwner="o1" };
            var owner=new OwnerModel{ Id="o1", Name="Alice", Address="X" };
            var img=new PropertyImageModel{ IdProperty="p1", Enabled=true, File="base64" };
            var traces=new List<PropertyTraceModel>{
                new PropertyTraceModel{ IdProperty="p1", Name="venta1" },
                new PropertyTraceModel{ IdProperty="p1", Name="venta2" }
            };

            SetupFindOne(_props, prop);
            SetupFindOne(_owners, owner);
            SetupFindOne(_images, img);
            SetupFindMany(_traces, traces);

            var res=await _sut.GetByIdAsync("p1");
            res.Success.Should().BeTrue();
            res.Data.Should().NotBeNull();
            res.Data!.Owner.Should().NotBeNull();
            res.Data!.Image.Should().NotBeNull();
            res.Data!.Traces!.Count.Should().Be(2);
        }

        [Test]
        public async Task CreateAsync_ShouldValidate_Insert_CreateOwnerImageTraces_AndReturn201()
        {
            var dto=new PropertyDto{
                Name="Depto", Address="Calle 1", Price=2000L, Year=2024, CodeInternal=11,
                Owner=new OwnerDto{ Name="Bob" },
                Image=new PropertyImageDto{ File="base64img", Enabled=true },
                Traces=new List<PropertyTraceDto>{ new PropertyTraceDto{ Name="hist-1" } }
            };

            _validator.Setup(v=>v.ValidateAsync(It.IsAny<PropertyDto>(),It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _ownerSvc.Setup(s => s.CreateAsync(It.IsAny<OwnerDto>()))
                     .ReturnsAsync(ServiceResultWrapper<OwnerDto>.Created(new OwnerDto{ IdOwner="o777", Name="Bob" },"ok"));
            _imgSvc.Setup(s=>s.CreateAsync(It.IsAny<PropertyImageDto>()))
                   .ReturnsAsync(ServiceResultWrapper<PropertyImageDto>.Created(null,"ok"));
            _traceSvc.Setup(s=>s.CreateAsync(It.IsAny<List<PropertyTraceDto>>()))
                     .Returns(Task.FromResult(ServiceResultWrapper<List<string>>.Created(new List<string>(), "ok")));

            _props.Setup(c=>c.InsertOneAsync(It.IsAny<PropertyModel>(),It.IsAny<InsertOneOptions>(),It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            SetupFindOne(_props, new PropertyModel{ Id="p-created", Name=dto.Name, Address=dto.Address, Price=dto.Price, Year=dto.Year, CodeInternal=dto.CodeInternal, IdOwner="o777" });
            SetupFindOne(_owners, new OwnerModel{ Id="o777", Name="Bob" });
            SetupFindOne(_images, new PropertyImageModel{ IdProperty="p-created", Enabled=true, File="base64img" });
            SetupFindMany(_traces, new List<PropertyTraceModel>{ new PropertyTraceModel{ IdProperty="p-created", Name="hist-1" } });

            var res=await _sut.CreateAsync(dto);

            res.StatusCode.Should().Be(201);
            res.Success.Should().BeTrue();
            res.Message.Should().Contain("creada");

            _ownerSvc.Verify(s=>s.CreateAsync(It.Is<OwnerDto>(o=>o.Name=="Bob")),Times.Once);
            _imgSvc.Verify(s=>s.CreateAsync(It.Is<PropertyImageDto>(i=>i.File=="base64img" && i.Enabled)),Times.Once);
            _traceSvc.Verify(s=>s.CreateAsync(It.Is<List<PropertyTraceDto>>(l=>l.Count==1)),Times.Once);
            _props.Verify(c=>c.InsertOneAsync(It.IsAny<PropertyModel>(),It.IsAny<InsertOneOptions>(),It.IsAny<CancellationToken>()),Times.Once);
        }

        [Test]
        public async Task DeleteAsync_ShouldReturnSuccess_WhenDeleted()
        {
            _props.Setup(c=>c.DeleteOneAsync(It.IsAny<FilterDefinition<PropertyModel>>(),It.IsAny<CancellationToken>())).ReturnsAsync(Del(1));
            _images.Setup(c=>c.DeleteManyAsync(It.IsAny<FilterDefinition<PropertyImageModel>>(),It.IsAny<CancellationToken>())).ReturnsAsync(Del(2));
            _traces.Setup(c=>c.DeleteManyAsync(It.IsAny<FilterDefinition<PropertyTraceModel>>(),It.IsAny<CancellationToken>())).ReturnsAsync(Del(3));

            var res=await _sut.DeleteAsync("p1");
            res.Success.Should().BeTrue();
            res.Message.Should().Contain("eliminada");
        }

        [Test]
        public async Task DeleteAsync_ShouldReturn404_WhenNotFound()
        {
            _props.Setup(c=>c.DeleteOneAsync(It.IsAny<FilterDefinition<PropertyModel>>(),It.IsAny<CancellationToken>())).ReturnsAsync(Del(0));
            _images.Setup(c=>c.DeleteManyAsync(It.IsAny<FilterDefinition<PropertyImageModel>>(),It.IsAny<CancellationToken>())).ReturnsAsync(Del(0));
            _traces.Setup(c=>c.DeleteManyAsync(It.IsAny<FilterDefinition<PropertyTraceModel>>(),It.IsAny<CancellationToken>())).ReturnsAsync(Del(0));

            var res=await _sut.DeleteAsync("p404");
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }
    }
}
