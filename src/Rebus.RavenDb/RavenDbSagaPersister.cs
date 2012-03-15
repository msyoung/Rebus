using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Exceptions;
using Raven.Client;
using Raven.Json.Linq;

namespace Rebus.RavenDb
{
    public class RavenDbSagaPersister : IStoreSagaData
    {
        readonly string collectionName;
        readonly Dictionary<ISagaData, Guid> etags = new Dictionary<ISagaData, Guid>();
        readonly IDocumentStore store;

        public RavenDbSagaPersister(IDocumentStore store, string collectionName)
        {
            this.store = store;
            this.collectionName = collectionName;
        }

        public void Save(ISagaData sagaData, string[] sagaDataPropertyPathsToIndex)
        {
            var etag = GetEtag(sagaData);
            try
            {
                store.DatabaseCommands.Put(Key(sagaData),
                                           etag,
                                           RavenJObject.FromObject(sagaData),
                                           new RavenJObject
                                           {
                                               { "Raven-Entity-Name", collectionName }
                                           });
            }
            catch (ConcurrencyException)
            {
                throw new OptimisticLockingException(sagaData);
            }
        }

        Guid? GetEtag(ISagaData sagaData)
        {
            Guid? etag = null;
            Guid existingEtag;
            if (etags.TryGetValue(sagaData, out existingEtag))
            {
                etag = existingEtag;
            }
            return etag;
        }

        public void Delete(ISagaData sagaData)
        {
            store.DatabaseCommands.Delete(Key(sagaData), GetEtag(sagaData));
            etags.Remove(sagaData);
        }

        public ISagaData Find(string sagaDataPropertyPath, object fieldFromMessage, Type sagaDataType)
        {
            QueryResult result;
            do
            {
                result = store.DatabaseCommands.Query("dynamic/" + collectionName,
                                                      new IndexQuery
                                                      {
                                                          Query = sagaDataPropertyPath + ":\"" + fieldFromMessage + "\""
                                                      },
                                                      new string[0]);
            } while (result.IsStale);

            var ravenJObject = result.Results.SingleOrDefault();
            if (ravenJObject == null)
                return null;

            var sagaData = (ISagaData) store.Conventions.CreateSerializer().Deserialize(new RavenJTokenReader(ravenJObject), sagaDataType);
            if (sagaData != null)
                etags[sagaData] = ravenJObject["@metadata"].Value<Guid>("@etag");

            return sagaData;
        }

        string Key(ISagaData sagaData)
        {
            return string.Format("{0}/{1}", collectionName, sagaData.Id);
        }
    }
}