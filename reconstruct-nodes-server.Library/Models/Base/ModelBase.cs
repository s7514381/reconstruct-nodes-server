using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models.Enum;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Library.Models
{
    public abstract class ModelBase
    {
        private readonly Dictionary<string, object> _properties = new Dictionary<string, object>();

        public ModelBase()
        {
            Id = Guid.NewGuid();
            CreateDate = DateTime.Now;
            Status = EStatus.啟用;
        }

        [ExplicitKey]
        public Guid Id { get => Get<Guid>(); set => Set(value); }
        public int? Sequence { get => Get<int?>(); set => Set(value); }
        public virtual DateTime? CreateDate { get => Get<DateTime?>(); set => Set(value); }

        [Relation(nameof(Player))]
        public virtual Guid? CreateUserId { get => Get<Guid?>(); set => Set(value); }
        public virtual EStatus? Status { get => Get<EStatus?>(); set => Set(value); }

        public virtual void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity(GetType()).HasIndex(nameof(CreateUserId));
        }

        protected bool Set<T>(T value, [CallerMemberName] string propertyName = null)
        {
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));

            if (_properties.TryGetValue(propertyName, out object currentValue) &&
                EqualityComparer<T>.Default.Equals(currentValue is T ? (T)currentValue : default, value))
                return false;

            _properties[propertyName] = value;
            return true;
        }

        protected T Get<T>([CallerMemberName] string propertyName = null)
        {
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));

            if (_properties.TryGetValue(propertyName, out object value) && value != null)
                return (T)value;

            return default(T);
        }
    }
}