using Firebase.Database;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Newtonsoft.Json;
using System.Reactive.Linq;

namespace ReconstructNodesServer.Services
{
    public class FirebaseService
    {
        private IServiceScopeFactory _scopeFactory;
        private FirestoreDb _db;
        private FirebaseClient _client;

        public FirestoreDb FireStore => _db;
        public FirebaseClient RealtimeDB => _client;

        public FirebaseService(IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;

            var credential = GoogleCredential.GetApplicationDefault();

            // 初始化 Firebase 應用程式
            FirebaseApp.Create(new AppOptions()
            {
                Credential = credential,
            });

            // 建立 Firestore 客戶端
            _db = FirestoreDb.Create("timwu19950429", new FirestoreClientBuilder
            {
                Credential = credential
            }.Build());

            // 建立 RealtimeDB 客戶端
            FirebaseOptions options = new FirebaseOptions
            {
                AuthTokenAsyncFactory = async () =>
                {
                    // 使用相同的憑證獲取訪問令牌
                    var scopedCredential = credential.CreateScoped("https://www.googleapis.com/auth/firebase.database");

                    var accessToken = await ((ITokenAccess)scopedCredential).GetAccessTokenForRequestAsync();
                    return accessToken;
                }
            };
            _client = new FirebaseClient("https://timwu19950429-default-rtdb.firebaseio.com/", options);
        }

        public async Task AddDataAsync()
        {
            // 建立一個文件參考
            DocumentReference docRef = _db.Collection("users").Document("user1");

            // 建立要新增的資料
            var user = new
            {
                Name = "John Doe",
                Email = "john@example.com",
                CreatedAt = Timestamp.GetCurrentTimestamp(),
                IsActive = true,
                Age = 30
            };

            // 新增文件
            await docRef.SetAsync(user);
            Console.WriteLine("資料新增成功");
        }

        public async Task ReadDataAsync()
        {
            // 取得文件參考
            DocumentReference docRef = _db.Collection("users").Document("user1");

            // 取得文件快照
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

            if (snapshot.Exists)
            {
                Console.WriteLine($"文件資料: {snapshot.Id}");
                Dictionary<string, object> user = snapshot.ToDictionary();

                foreach (KeyValuePair<string, object> pair in user)
                {
                    Console.WriteLine($"{pair.Key}: {pair.Value}");
                }
            }
            else
            {
                Console.WriteLine("文件不存在");
            }
        }

        public async Task UpdateDataAsync()
        {
            DocumentReference docRef = _db.Collection("users").Document("user1");

            // 更新特定欄位
            var updates = new Dictionary<string, object>
            {
                { "Age", 31 },
                { "UpdatedAt", Timestamp.GetCurrentTimestamp() }
            };

            await docRef.UpdateAsync(updates);
            Console.WriteLine("資料更新成功");
        }

        public async Task DeleteDataAsync()
        {
            DocumentReference docRef = _db.Collection("users").Document("user1");
            await docRef.DeleteAsync();
            Console.WriteLine("資料刪除成功");
        }

        public async Task RealtimeUpdatesAsync()
        {
            CollectionReference collectionRef = _db.Collection("users");

            // 建立監聽器
            FirestoreChangeListener listener = collectionRef.Listen(snapshot =>
            {
                Console.WriteLine("\n偵測到集合變更:");
                foreach (Google.Cloud.Firestore.DocumentChange change in snapshot.Changes)
                {
                    DocumentSnapshot document = change.Document;
                    Console.WriteLine($"變更類型: {change.ChangeType}");
                    Console.WriteLine($"文件ID: {document.Id}");

                    if (document.Exists)
                    {
                        Console.WriteLine("文件內容:");
                        Dictionary<string, object> data = document.ToDictionary();
                        foreach (KeyValuePair<string, object> pair in data)
                        {
                            Console.WriteLine($"{pair.Key}: {pair.Value}");
                        }
                    }
                }
            });

            Console.WriteLine("監聽器已啟動，按任意鍵停止...");
            Console.ReadKey();

            // 停止監聽
            await listener.StopAsync();
            Console.WriteLine("監聽器已停止");
        }

        // 讀取資料範例
        public async Task<T> GetDataAsync<T>(string path)
        {
            return await _client
                .Child(path)
                .OnceSingleAsync<T>();
        }

        // 寫入資料範例
        public async Task SetDataAsync<T>(string path, T data)
        {
            await _client
                .Child(path)
                .PutAsync(JsonConvert.SerializeObject(data));
        }

        // 更新資料範例
        public async Task UpdateDataAsync<T>(string path, T data)
        {
            await _client
                .Child(path)
                .PatchAsync(JsonConvert.SerializeObject(data));
        }

        // 刪除資料範例
        public async Task DeleteDataAsync(string path)
        {
            await _client
                .Child(path)
                .DeleteAsync();
        }

        // 監聽資料變化範例
        public IDisposable SubscribeToChanges<T>(string path, Action<T> dataChangedCallback)
        {
            return _client
                .Child(path)
                .AsObservable<T>()
                .Subscribe(x => dataChangedCallback(x.Object));
        }

        public IDisposable SubscribeToChangesAsync<T>(string path, Func<FirebaseObject<T>, Task> dataChangedCallback)
        {
            return _client
                .Child(path)
                .AsObservable<T>()
                .SubscribeAsync(dataChangedCallback);
        }

    }

    public static class FirebaseExtensions
    {
        public static IDisposable SubscribeAsync<T>(
            this IObservable<FirebaseObject<T>> observable,
            Func<FirebaseObject<T>, Task> onNextAsync,
            Action<Exception>? onError = null,
            Action? onCompleted = null)
        {
            return observable
                .Select(item => Observable.FromAsync(() => onNextAsync(item)))
                .Concat()
                .Subscribe(
                    _ => { }, // 忽略實際結果，因為我們已經在 onNextAsync 中處理了
                    onError ?? (ex => Console.WriteLine($"Error: {ex.Message}")),
                    onCompleted ?? (() => Console.WriteLine("Completed")));
        }
    }



}

