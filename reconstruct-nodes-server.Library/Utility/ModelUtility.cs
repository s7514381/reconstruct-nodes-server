using Newtonsoft.Json;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Library.Utility
{
    public static class ModelUtility
    {
        public static TViewModel MapSetting<TViewModel, TSetting>(this TSetting setting)
            where TSetting : ModelBase
            where TViewModel : ModelBase, new()
        {
            TViewModel viewModel = setting.CastBy<TViewModel>();
            viewModel.Id = Guid.NewGuid();

            string foreignKeyField = CommonUtility.GetForeignKeyField<TViewModel, TSetting>();
            viewModel.SetFieldValue(foreignKeyField, setting.Id);

            return viewModel;
        }

        public static void MapSetting<TViewModel, TSetting>(this TViewModel model, TSetting setting)
            where TSetting : ModelBase
            where TViewModel : ModelBase
        {
            model.Map(setting);
            model.Id = Guid.NewGuid();

            string foreignKeyField = CommonUtility.GetForeignKeyField<TViewModel, TSetting>();
            model.SetFieldValue(foreignKeyField, setting.Id);
        }

        public static async Task<bool> ListTransaction<T>(this List<T> list, Func<T, bool> predicate, Func<List<T>, Task<bool>> func)
        {
            var predicateList = list.Where(predicate).ToList();
            if (predicateList.Count == 0) { return true; }

            List<T> tran = predicateList.DeepCopy();
            bool result = await func(tran);
            if (result) 
            { 
                list.RemoveAll(new Predicate<T>(predicate));
                list.Map(tran);
            }
            return result;
        }

        public static async Task<bool> FirstTransaction<T>(this List<T> list, Func<T, bool> predicate, Func<T, Task<bool>> func)
        {
            T item = list.FirstOrDefault(predicate);
            if (item == null) return true; //沒有篩選到的話視同做完

            int index = list.IndexOf(item);

            T tranItem = item.DeepCopy();

            bool result = await func(tranItem);
            if (result) { list[index] = tranItem; }

            return result;
        }

        public static async Task<bool> ModelTransaction<T>(this T item, Func<T, Task<bool>> func)
        {
            T tranItem = item.DeepCopy();

            bool result = await func(tranItem);
            if (result) { item.Map(tranItem); }

            return result;
        }

    }
}
