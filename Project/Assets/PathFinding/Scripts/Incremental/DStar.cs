﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Priority_Queue;

/// <summary>
/// D* 算法
/// 
/// 一些调整：
/// * 论文里使用的h容易误解为启发函数，但实际表示到目标点的最短距离
/// * 我这里用parent表示论文里的backpointer
/// 
/// 注意事项：
/// 论文里阻挡并不是完全不可走的，只移动代价非常大
/// 
/// Demo的玩耍方式：把Show Time参数弄大点，这样可以看到格子的移动，然后在它前面加上阻挡看看效果
/// </summary>
public class DStar : BaseSearchAlgo
{
    private const int c_sensorRadius = 2;

    private readonly int m_largeValue; //用于阻挡的代价，普通算出来的移动代价一定要比该值小
    private readonly int[,] m_foundMap; //目前通过传感器发现的地图
    private readonly SimplePriorityQueue<SearchNode> m_openQueue = new SimplePriorityQueue<SearchNode>();

    private SearchNode m_currR;

    public DStar(SearchNode start, SearchNode goal, SearchNode[,] nodes, float showTime)
        : base(start, goal, nodes, showTime)
    {
        m_largeValue = m_mapWidth * m_mapHeight * 10;
        m_foundMap = new int[nodes.GetLength(0), nodes.GetLength(1)];
    }

    private float ProcessState()
    {
        SearchNode X = MinState();
        if (X == null)
            return -1;

        float kOld = GetKMin();
        Delete(X);

        //发现RAISE，则检查能否通过邻居获得更短的路径
        if(Less(kOld, X.H))
        {
            List<SearchNode> neighbors = GetNeighbors(X);
            for(int i = 0; i < neighbors.Count; i++)
            {
                SearchNode Y = neighbors[i];
                if(LessEqual(Y.H, kOld) && Bigger(X.H, (Y.H + C(Y, X))))
                {
                    X.Parent = Y;
                    X.H = Y.H + C(Y, X);
                }
            }
        }

        if(Equal(kOld, X.H)) //LOWER state
        {
            List<SearchNode> neighbors = GetNeighbors(X);
            for(int i = 0; i < neighbors.Count; i++)
            {
                SearchNode Y = neighbors[i];
                if(Y.IsNew ||
                    (Y.Parent == X && NotEqual(Y.H, (X.H+ C(X, Y)))) ||
                    (Y.Parent != X && Bigger(Y.H, (X.H + C(X, Y)))))
                {
                    Y.Parent = X;
                    Insert(Y, X.H + C(X, Y));
                }
            }
        }
        else //RAISE state
        {
            List<SearchNode> neighbors = GetNeighbors(X);
            for(int i = 0; i < neighbors.Count; i++)
            {
                SearchNode Y = neighbors[i];
                if(Y.IsNew || (Y.Parent == X && NotEqual(Y.H, (X.H + C(X, Y)))))
                {
                    Y.Parent = X;
                    Insert(Y, X.H + C(X, Y));
                }
                else
                {
                    if(Y.Parent != X && Bigger(Y.H, (X.H + C(X, Y))))
                    {
                        Insert(X, X.H);
                    }
                    else
                    {
                        if (Y.Parent != X && Bigger(X.H, (Y.H + C(Y, X))) && Y.Closed && Bigger(Y.H, kOld))
                            Insert(Y, Y.H);
                    }
                }
            }
        }

        return GetKMin();
    }

    private SearchNode MinState()
    {
        if (m_openQueue.Count > 0)
            return m_openQueue.First;
        else
            return null;
    }

    private float GetKMin()
    {
        if (m_openQueue.Count > 0)
            return m_openQueue.FirstPriority;
        else
            return -1;
    }

    private void Delete(SearchNode node)
    {
        m_openQueue.Remove(node);
        node.Closed = true;
    }

    private void Insert(SearchNode node, float newG)
    {
        if (node.IsNew)
            node.Key = newG;
        else if (node.Opened)
            node.Key = Mathf.Min(node.Key, newG);
        else if (node.Closed)
            node.Key = Mathf.Min(node.H, newG);

        node.Opened = true;
        node.H = newG;

        if (m_openQueue.Contains(node))
            m_openQueue.UpdatePriority(node, node.Key);
        else
            m_openQueue.Enqueue(node, node.Key);
    }

    private float ModifyCost(SearchNode node, byte cost)
    {
        node.SetCost(cost);

        if (node.Closed)
            Insert(node, node.H);

        return GetKMin();
    }

    public override IEnumerator Process()
    {
        //假设一开始通过卫星等方式获取了原始地图
        //这里我通过记录原本的格子代价来判断之后有没有发生过变化
        //如果是通过其他方式检测格子变化，则可以去掉这部分代码从而节省内存
        for (int y = 0; y < m_mapHeight; y++)
            for (int x = 0; x < m_mapWidth; x++)
                m_foundMap[y, x] = m_nodes[y, x].Cost;

        //寻路开始
        m_currR = m_start;
        Insert(m_goal, 0);
        InitPlanPath();

        while (m_currR != m_goal)
        {
            //往前走一步
            if(MoveForwardOneStep() == false)
            {
                Debug.LogError("寻找不到路径，遇见了障碍");
                yield break;
            }

            //通过传感器看看检查周围的环境是否变化
            CheckNearChanged();

            yield return new WaitForSeconds(m_showTime);
        }

        yield break;
    }

    private bool MoveForwardOneStep()
    {
        if (m_currR.Parent == null)
            return false;

        //路上遇见阻挡，则表示没有路径
        if (C(m_currR, m_currR.Parent) >= m_largeValue)
            return false;

        m_currR.SetSearchType(SearchType.Path, true);
        m_currR = m_currR.Parent;
        m_currR.SetSearchType(SearchType.CurtPos, true);

        return true;
    }

    private void InitPlanPath()
    {
        float result = 0;
        while (result >= 0 && !m_currR.Closed)
        {
            result = ProcessState();
        }
    }

    private void ReplanPath()
    {
        float result = 0;
        while (result >= 0)
            result = ProcessState();
    }

    private bool IsFoundObstacle(int x, int y)
    {
        //假设并不知道整张地图的情况，那么只能依赖当前发现的格子代价来作为判断依据
        return m_foundMap[y, x] == Define.c_costObstacle;
    }

    /// <summary>
    /// 从节点b到节点a的代价（对于有向图来说，顺序很重要）
    /// </summary>
    private float C(SearchNode a, SearchNode b)
    {
        if(IsFoundObstacle(a.X, a.Y) || IsFoundObstacle(b.X, b.Y))
        {
            return m_largeValue;
        }
        else
        {
            //走斜线时，如果两边都是阻挡，那么该斜线的代价也是阻挡那么大
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            if(Mathf.Abs(dx) != 0 && Mathf.Abs(dy) != 0)
            {
                if (IsFoundObstacle(b.X + dx, b.Y) && IsFoundObstacle(b.X, b.Y + dy))
                    return m_largeValue;
            }

            return CalcCost(a, b);
        }
    }

    private void CheckNearChanged()
    {
        List<SearchNode> nearChanged = new List<SearchNode>();

        //假设检测器只能检查附近的点
        List<SearchNode> nearNodes = SensorDetectNodes(c_sensorRadius);
        for (int i = 0; i < nearNodes.Count; i++)
        {
            Vector2Int pos = nearNodes[i].Pos;
            if (nearNodes[i].Cost != m_foundMap[pos.y, pos.x])
            {
                m_foundMap[pos.y, pos.x] = nearNodes[i].Cost;
                nearChanged.Add(nearNodes[i]);
            }
        }

        //如果发现有格子发生变化
        if(nearChanged.Count > 0)
        {
            for(int i = 0; i < nearChanged.Count; i++)
            {
                SearchNode node = nearChanged[i];
                ModifyCost(node, node.Cost);
                ForeachNeighbors(node, (n) => { ModifyCost(n, n.Cost); }); 
            }

            //重新计划路径
            ReplanPath();
        }
    }

    /// <summary>
    /// 传感器能检测到的格子
    /// </summary>
    /// <param name="radius">检测的范围</param>
    /// <returns>能检测到的格子</returns>
    private List<SearchNode> SensorDetectNodes(int radius)
    {
        List<SearchNode> result = new List<SearchNode>();

        for(int dx = -radius; dx <= radius; dx++)
            for(int dy = -radius; dy <= radius; dy++)
                TryAddNode(m_currR.Pos, dx, dy, result);

        return result;
    }

    protected override bool TryAddNode(Vector2Int curtPos, int dx, int dy, List<SearchNode> result)
    {
        int x = curtPos.x + dx;
        int y = curtPos.y + dy;
        SearchNode node = GetNode(x, y);
        if (node != null) //原始论文中障碍物只是代价非常高，但还是可以作为邻居
        {
            result.Add(node);
            return true;
        }
        else
        {
            return false;
        }
    }
}
