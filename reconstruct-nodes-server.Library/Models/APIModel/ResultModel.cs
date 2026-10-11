using System;
using System.Collections.Generic;
using System.Text;

namespace ReconstructNodesServer.Library.Models
{
    public class ResultModel<T>
    {
        public ResultModel() 
        {
            Data = default(T); 
        }

        public bool IsSuccess { get; set; }

        public string Code { get; set; }

        public T Data { get; set; }
    }

    public static class ResultModelExtension 
    { 
        public static void Success<T>(this ResultModel<T> model, T data) 
        {
            model.IsSuccess = true;
            model.Data = data;
        }

        public static void Error<T>(this ResultModel<T> model, T data)
        {
            model.IsSuccess = false;
            model.Data = data;
        }
        public static void Error<T>(this ResultModel<T> model)
        {
            model.IsSuccess = false;
        }
    }

}
