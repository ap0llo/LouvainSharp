﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace LouvainCommunityPL
{
    /// <summary>
    /// This class implements community detection.
    /// Adapted from python-louvain, http://perso.crans.org/aynaud/communities/, by Kyle Miller (v-kymil@microsoft.com)
    /// February 2014
    /// Original copyright:
    /// Copyright (C) 2009 by
    /// Thomas Aynaud
    /// <thomas.aynaud@ lip6.fr>
    /// All rights reserved.
    /// BSD license.
    /// </summary>
    public static class Community
    {
        internal static int PASS_MAX = -1;
        internal static double MIN = 0.0000001;

        /// <summary>
        /// Compute the partition of the graph nodes which maximises the modularity using the Louvain heuristics (or try...)
        /// This is the partition of the highest modularity, i.e., the highest partition of the dendrogram generated by the Louvain
        /// algorithm.
        /// See also: GenerateDendrogram to obtain all the decomposition levels
        /// Notes: Uses the Louvain algorithm
        /// References:
        /// 1. Blondel, V.D. et al. Fast unfolding of communities in large networks. J. Stat. Mech 10008, 1-12(2008).
        /// </summary>
        /// <param name="graph">The graph which is decomposed.</param>
        /// <param name="partition">
        /// The algorithm will start using this partition of nodes. It is a dictionary where keys are nodes
        /// and values are communities.
        /// </param>
        /// <returns>The partition, with communities number from 0 onward, sequentially</returns>
        public static Dictionary<int, int> BestPartition(IGraph graph)
        {
            Dendrogram dendro = GenerateDendrogram(graph);
            return dendro.PartitionAtLevel(dendro.Length - 1);
        }

        static Dendrogram GenerateDendrogram(IGraph graph)
        {
            IDictionary<int, int> partition;

            // Special case, when there is no link, the best partition is everyone in its own community.
            if (graph.NumberOfEdges == 0)
            {
                partition = new Dictionary<int, int>();
                int i = 0;
                foreach (int node in graph.Nodes)
                {
                    partition[node] = i++;
                }

                return new Dendrogram(partition);
            }

            var current_graph = graph;

            Status status = new Status(current_graph);
            double mod = status.Modularity;
            var status_list = new List<IDictionary<int, int>>();
            OneLevel(current_graph, status);
            double new_mod;
            new_mod = status.Modularity;
            
            do
            {
                partition = Renumber(status.CurrentPartition);
                status_list.Add(partition);
                mod = new_mod;
                current_graph = current_graph.GetQuotient(partition);
                status = new Status(current_graph);
                OneLevel(current_graph, status);
                new_mod = status.Modularity;
            }
            while (new_mod - mod >= MIN);

            return new Dendrogram(status_list);
        }

        /// <summary>
        /// Renumbers the communities in the specified partition
        /// so there are no "gaps" in the range of community numbers
        /// </summary>        
        /// <param name="partition">The partition of a graph into communities</param>
        /// <returns></returns>
        private static Dictionary<int, int> Renumber(IReadOnlyDictionary<int, int> partition)
        {
            var renumberedPartition = new Dictionary<int, int>();
            var newCommunityIds = new Dictionary<int, int>();
            int nextNewCommunityId = 0;

            foreach (var nodeId in partition.Keys.OrderBy(a => a))
            {
                var oldCommunityId = partition[nodeId];
                
                if (!newCommunityIds.ContainsKey(oldCommunityId))
                {
                    newCommunityIds.Add(oldCommunityId, nextNewCommunityId);
                    nextNewCommunityId += 1;
                }
                renumberedPartition[nodeId] = newCommunityIds[oldCommunityId];
            }
            return renumberedPartition;
        }


        /// <summary>
        /// Compute one level of communities.
        /// </summary>
        /// <param name="graph">The graph to use.</param>
        static void OneLevel(IGraph graph, Status status)
        {
            bool modif = true;
            int nb_pass_done = 0;
            double cur_mod = status.Modularity;
            double new_mod = cur_mod;

            while (modif && nb_pass_done != Community.PASS_MAX)
            {
                cur_mod = new_mod;
                modif = false;
                nb_pass_done += 1;

                foreach (int node in graph.Nodes)
                {
                    int com_node = status.CurrentPartition[node];
                    double degc_totw = status.GDegrees.GetValueOrDefault(node) / (status.TotalWeight * 2);
                    Dictionary<int, double> neigh_communities = status.NeighCom(node, graph);
                    status.Remove(node, com_node, neigh_communities.GetValueOrDefault(com_node));

                    Tuple<double, int> best;
                    best = (from entry in neigh_communities.AsParallel()
                            select EvaluateIncrease(status, entry.Key, entry.Value, degc_totw))
                        .Concat(new[] { Tuple.Create(0.0, com_node) }.AsParallel())
                        .Max();
                    int best_com = best.Item2;
                    status.Insert(node, best_com, neigh_communities.GetValueOrDefault(best_com));
                    if (best_com != com_node)
                    {
                        modif = true;
                    }
                }
                new_mod = status.Modularity;
                if (new_mod - cur_mod < MIN)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Used in parallelized OneLevel
        /// </summary>
        static Tuple<double, int> EvaluateIncrease(Status status, int com, double dnc, double degc_totw)
        {
            double incr = dnc - status.GetCommunityDegree(com) * degc_totw;
            return Tuple.Create(incr, com);
        }
    }


}