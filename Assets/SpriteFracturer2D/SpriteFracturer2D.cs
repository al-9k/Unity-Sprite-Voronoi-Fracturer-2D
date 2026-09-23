/*
 * SpriteFracturer2D.cs
 * 
 * Original Author: Parein Jean-Philippe
 * Forked and modified By: Alhasan Shnoot
 * Version: 1.1 (Voronoi Refactor)
 * 
 * Description:
 *  Runtime 2D sprite fracturing system for Unity. 
 *  Splits a sprite into procedural shards using Voronoi diagram partitioning,
 *  featuring physics-based explosions, optional blinking effects, timed destruction, 
 *  and editor utilities.
 *
 * Changelog / Modifications:
 *   - [v1.1] Refactored core Fracture() function to generate shards using 
 *            a Voronoi algorithm for more organic procedural cell generation.
 *   - [v1.0] Original base script by Parein Jean-Philippe.
 *
 * Compatibility:
 *   - Works with all Unity render pipelines:
 *       Built-in Render Pipeline
 *       Universal Render Pipeline (URP)
 *       High Definition Render Pipeline (HDRP, with 2D Renderer)
 *   - Requires the sprite's texture to be Read/Write Enabled in import settings.
 * 
 * Tested with:
 *   - Unity 2022.3+
 *   - Unity 6 (URP 2D)
 * 
 * License: MIT
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpriteFracture
{
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class SpriteFracturer2D : MonoBehaviour
    {
        public enum TriggerMode { AutoStart, Collision, Trigger, Manual }

        [Header("Trigger Mode")]
        public TriggerMode triggerMode = TriggerMode.AutoStart;

        [Header("Fracture Settings")]
        public int siteCount = 7;
        public int  = 1337;

        [Header("Physics Settings")]
        public float pieceMass = 0.2f;
        public float explosionForce = 300f;
        public float upwardModifier = 0.5f;

        [Header("Lifetime / Destruction")]
        public float pieceLifetime = 5f;
        public bool destroyPieces = true;
        public bool destroyOnCollision = false;
        public float collisionArmDelay = 0.05f;

        [Header("Blink Effect")]
        public bool useBlink = true;
        public float blinkDuration = 1f;
        public float blinkFrequency = 10f;

        [Header("Collider Options")]
        public bool piecesAsTrigger = false;

        [Header("Events")]
        public UnityEvent onFracture;
        public UnityEvent onPieceDestroyed;

        [Header("Misc")]
        public float delayBeforeFracture = 1f;
        public bool showGridGizmos = true;

        private SpriteRenderer sr;
        private bool fractured = false;

#if UNITY_EDITOR
        private List<Vector2Int> cachedGizmoSites;
        private int lastGizmoSiteCount;
        private int lastGizmo;
        private Sprite lastGizmoSprite;
#endif

        #region External Event Subscription Example
        /* 
         * EXAMPLE: How to trigger fracturing from custom game events.
         * You can adapt this pattern to subscribe to your own health, armor, or damage events.
         * 
         * void OnEnable()
         * {
         *     // MyGameEvents.OnObjectShattered += OnExternalShatterEvent;
         * }
         * 
         * void OnDisable()
         * {
         *     // MyGameEvents.OnObjectShattered -= OnExternalShatterEvent;
         * }
         * 
         * private void OnExternalShatterEvent(GameObject target)
         * {
         *     // Verify if this event targets this object or one of its parents
         *     if (target == gameObject || transform.IsChildOf(target.transform))
         *     {
         *         TriggerFracture();
         *     }
         * }
         */
        #endregion
        
        /// <summary>
        /// Public entry point to manually trigger the fracture effect from external scripts or events.
        /// </summary>
    
        public void TriggerFracture()
        {
            if (triggerMode == TriggerMode.Manual && !fractured)
            {
                StartCoroutine(Fracture());
            }
        }

        void Start()
        {
            sr = GetComponent<SpriteRenderer>();

            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0;
            rb.bodyType = triggerMode == TriggerMode.Collision
                ? RigidbodyType2D.Dynamic
                : RigidbodyType2D.Kinematic;
            rb.freezeRotation = true;

            PolygonCollider2D col = GetComponent<PolygonCollider2D>();
            col.isTrigger = triggerMode == TriggerMode.Trigger;

            if (triggerMode == TriggerMode.AutoStart)
                StartCoroutine(FractureAfterDelay());
        }

        private IEnumerator FractureAfterDelay()
        {
            yield return new WaitForSeconds(delayBeforeFracture);
            StartCoroutine(Fracture());
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (triggerMode != TriggerMode.Collision || fractured) return;
            fractured = true;
            StartCoroutine(Fracture());
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (triggerMode != TriggerMode.Trigger || fractured) return;
            fractured = true;
            StartCoroutine(Fracture());
        }

        public List<Vector2Int> GenerateSites(int n, Sprite text, int?  = null)
        {   
            List<Vector2Int> validPixels = new List<Vector2Int>();
            List<Vector2Int> sites = new List<Vector2Int>();

            Rect rect = text.textureRect;
            Texture2D tex = text.texture;

            Color[] pixels = tex.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height);
            int width = (int)rect.width;

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 0.01f)
                {
                    int x = (int)rect.x + (i % width);
                    int y = (int)rect.y + (i / width);
                    validPixels.Add(new Vector2Int(x, y));
                }
            }

            if (validPixels.Count == 0) return sites;

            // Preserve state so editor preview doesn't break global random
            Random.State prevState = Random.state;
            if (seed.HasValue) Random.InitState(seed.Value);

            int count = Mathf.Min(n, validPixels.Count);
            for (int i = 0; i < count; i++)
            {
                int randomIndex = Random.Range(0, validPixels.Count);
                sites.Add(validPixels[randomIndex]);
                validPixels.RemoveAt(randomIndex);
            }

            if (seed.HasValue) Random.state = prevState;

            return sites;
        }

        public int GetNearestNeighbours(List<Vector2Int> sites, Vector2Int coord)
        {   
            int minDistSq = int.MaxValue;
            int minIndex = 0;

            for (int i = 0; i < sites.Count; i++)
            {   
                int xTerm = sites[i].x - coord.x;
                int yTerm = sites[i].y - coord.y;
                int distSq = xTerm * xTerm + yTerm * yTerm;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    minIndex = i;
                } 
            }
            return minIndex;
        }

        public IEnumerator Fracture()
        {
            if (sr == null || sr.sprite == null || fractured)
                yield break;

            Texture2D sourceTex = sr.sprite.texture;
            if (!sourceTex.isReadable)
            {
                Debug.LogError("Texture not readable: enable 'Read/Write Enabled' in import settings.");
                yield break;
            }

            fractured = true;
            onFracture?.Invoke();

            Collider2D mainCol = GetComponent<Collider2D>();
            if (mainCol) mainCol.enabled = false;

            Sprite sprite = sr.sprite;
            Rect texRect = sprite.textureRect;
            float ppu = sprite.pixelsPerUnit;
            sr.enabled = false;

            Vector3 origin = transform.position;

            GameObject parent = new GameObject("Fracture_" + gameObject.name);
            parent.transform.position = transform.position;

            List<Vector2Int> sites = GenerateSites(siteCount, sprite, seed);
            List<List<Vector2Int>> regions = new List<List<Vector2Int>>();

            for (int i = 0; i < sites.Count; i++)
                regions.Add(new List<Vector2Int>());

            Color[] rawSourcePixels = sourceTex.GetPixels(
                (int)texRect.x, 
                (int)texRect.y, 
                (int)texRect.width, 
                (int)texRect.height
            );
            int texWidth = (int)texRect.width;

            for (int y = (int)texRect.y; y < (int)texRect.yMax; y++) 
            {
                for (int x = (int)texRect.x; x < (int)texRect.xMax; x++) 
                {
                    int localX = x - (int)texRect.x; 
                    int localY = y - (int)texRect.y; 
                    int arrayIndex = localY * texWidth + localX;
                    if (rawSourcePixels[arrayIndex].a < 0.01f) continue;
                    
                    Vector2Int coord = new Vector2Int(x, y);
                    int closestSiteIndex = GetNearestNeighbours(sites, coord);
                    regions[closestSiteIndex].Add(coord);
                }
            }

            for (int i = 0; i < regions.Count; i++)
            {   
                if (regions[i].Count == 0) continue;
                
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;

                for (int j = 0; j < regions[i].Count; j++)
                {
                    Vector2Int p = regions[i][j];
                    if (p.x < minX) minX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y > maxY) maxY = p.y;
                }

                int width = maxX - minX + 1;
                int length = maxY - minY + 1;

                Texture2D shardTex = new Texture2D(width, length, TextureFormat.RGBA32, false);
                Color[] clearColors = new Color[width * length];
                shardTex.SetPixels(clearColors);

                for (int j = 0; j < regions[i].Count; j++)
                {
                    Vector2Int globalPos = regions[i][j];
                    Color pixelColor = sourceTex.GetPixel(globalPos.x, globalPos.y);
                    shardTex.SetPixel(globalPos.x - minX, globalPos.y - minY, pixelColor);
                }
                shardTex.Apply();

                Sprite pieceSprite = Sprite.Create(shardTex, new Rect(0, 0, width, length), new Vector2(0.5f, 0.5f), ppu);

                float localCenterX = (minX - sprite.rect.x) + (width / 2f);
                float localCenterY = (minY - sprite.rect.y) + (length / 2f);

                Vector3 localOffset = new Vector3(
                    (localCenterX - sprite.pivot.x) / ppu,
                    (localCenterY - sprite.pivot.y) / ppu,
                    0f
                );

                GameObject piece = new GameObject($"Piece_{i}");
                piece.transform.parent = parent.transform;
                piece.transform.position = transform.TransformPoint(localOffset);
                piece.transform.rotation = transform.rotation;
                piece.transform.localScale = transform.lossyScale;
                
                SpriteRenderer psr = piece.AddComponent<SpriteRenderer>();
                psr.sprite = pieceSprite;
                psr.sortingLayerID = sr.sortingLayerID;
                psr.sortingOrder = sr.sortingOrder;

                Rigidbody2D rb = piece.AddComponent<Rigidbody2D>();
                rb.mass = pieceMass;
                rb.gravityScale = 1f;
                rb.linearDamping = 0.5f;
                rb.angularDamping = 0.5f;

                PolygonCollider2D poly = piece.AddComponent<PolygonCollider2D>();
                poly.isTrigger = piecesAsTrigger;

                // Cleanup component handles asset destruction on removal
                piece.AddComponent<ShardCleanup>().Init(shardTex, pieceSprite);

                if (destroyOnCollision)
                {
                    PieceCollisionHandler handler = piece.AddComponent<PieceCollisionHandler>();
                    handler.Init(psr, parent.transform, useBlink, pieceLifetime, blinkDuration, blinkFrequency, collisionArmDelay, piecesAsTrigger, onPieceDestroyed);
                }
                else if (destroyPieces)
                {
                    if (useBlink)
                        piece.AddComponent<BlinkBeforeDestroy>().Init(psr, pieceLifetime, blinkDuration, blinkFrequency, onPieceDestroyed);
                    else
                        StartCoroutine(DestroyAfterDelay(piece, pieceLifetime));
                }

                Vector2 dir = ((Vector2)piece.transform.position - (Vector2)origin).normalized;
                dir = (dir + Random.insideUnitCircle * 0.3f).normalized;
                rb.AddForce((dir + Vector2.up * upwardModifier).normalized * explosionForce);
                rb.AddTorque(Random.Range(-100f, 100f));
            }

            Destroy(gameObject);
        }

        private IEnumerator DestroyAfterDelay(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            onPieceDestroyed?.Invoke();
            Destroy(obj);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showGridGizmos) return;
            var sr = GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null || !sr.sprite.texture.isReadable) return;

            Sprite sprite = sr.sprite;

            // Cache gizmo sites to avoid heavy texture iteration every frame
            if (cachedGizmoSites == null || lastGizmoSiteCount != siteCount || lastGizmoSeed != seed || lastGizmoSprite != sprite)
            {
                cachedGizmoSites = GenerateSites(siteCount, sprite, seed);
                lastGizmoSiteCount = siteCount;
                lastGizmoSeed = seed;
                lastGizmoSprite = sprite;
            }

            float ppu = sprite.pixelsPerUnit;
            Gizmos.color = Color.yellow;

            foreach (var site in cachedGizmoSites)
            {
                Vector3 localOffset = new Vector3(
                    (site.x - sprite.pivot.x) / ppu,
                    (site.y - sprite.pivot.y) / ppu,
                    0f
                );

                Vector3 worldPos = transform.TransformPoint(localOffset);
                Gizmos.DrawSphere(worldPos, 0.04f * transform.lossyScale.x);
            }
        }
#endif
    }

    // Prevents memory leaks by destroying runtime textures/sprites when shard is destroyed
    public class ShardCleanup : MonoBehaviour
    {
        private Texture2D tex;
        private Sprite sprite;

        public void Init(Texture2D t, Sprite s)
        {
            tex = t;
            sprite = s;
        }

        private void OnDestroy()
        {
            if (sprite) Destroy(sprite);
            if (tex) Destroy(tex);
        }
    }

    public class PieceCollisionHandler : MonoBehaviour
    {
        private SpriteRenderer sr;
        private Transform root;
        private bool useBlink;
        private float lifetime, blinkDuration, blinkFreq, armDelay;
        private bool armed = false;
        private bool asTrigger;
        private UnityEvent onPieceDestroyed;
        private bool isDestroying = false;

        public void Init(SpriteRenderer r, Transform parentRoot, bool blink, float life, float bDur, float bFreq, float armDelaySec, bool piecesAreTrigger, UnityEvent onDestroyed)
        {
            sr = r;
            root = parentRoot;
            useBlink = blink;
            lifetime = life;
            blinkDuration = bDur;
            blinkFreq = bFreq;
            armDelay = Mathf.Max(0f, armDelaySec);
            asTrigger = piecesAreTrigger;
            onPieceDestroyed = onDestroyed;
            StartCoroutine(ArmAfterDelay());
            if (lifetime > 0f) StartCoroutine(DestroyAfterLifetime());
        }

        private IEnumerator ArmAfterDelay() { yield return new WaitForSeconds(armDelay); armed = true; }
        private IEnumerator DestroyAfterLifetime() { yield return new WaitForSeconds(lifetime); TryDestroyNow(); }

        private bool IsSameFracture(Transform other) => other != null && other.parent == root;

        private void TryDestroyNow()
        {
            if (isDestroying || (!armed && lifetime > 0)) return;
            isDestroying = true;

            if (useBlink)
            {
                var blink = gameObject.AddComponent<BlinkBeforeDestroy>();
                blink.TriggerNow(blinkDuration, blinkFreq, onPieceDestroyed);
            }
            else
            {
                onPieceDestroyed?.Invoke();
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter2D(Collision2D col)
        {
            if (IsSameFracture(col.transform)) return;
            TryDestroyNow();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!asTrigger) return;
            if (IsSameFracture(other.transform)) return;
            TryDestroyNow();
        }
    }

    public class BlinkBeforeDestroy : MonoBehaviour
    {
        private SpriteRenderer sr;
        private UnityEvent onDestroyed;

        public void Init(SpriteRenderer target, float lifetime, float blinkDuration, float frequency, UnityEvent callback)
        {
            sr = target;
            onDestroyed = callback;
            StartCoroutine(BlinkTimed(lifetime, blinkDuration, frequency));
        }

        public void TriggerNow(float blinkDuration, float frequency, UnityEvent callback)
        {
            sr = GetComponent<SpriteRenderer>();
            onDestroyed = callback;
            StartCoroutine(BlinkNow(blinkDuration, frequency));
        }

        private IEnumerator BlinkTimed(float lifetime, float blinkDuration, float frequency)
        {
            yield return new WaitForSeconds(Mathf.Max(0, lifetime - blinkDuration));
            yield return BlinkNow(blinkDuration, frequency);
        }

        private IEnumerator BlinkNow(float duration, float frequency)
        {
            float elapsed = 0f;
            float interval = 1f / Mathf.Max(1f, frequency);
            bool visible = true;
            while (elapsed < duration)
            {
                if (!sr) yield break;
                visible = !visible;
                sr.enabled = visible;
                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }
            onDestroyed?.Invoke();
            Destroy(gameObject);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(SpriteFracturer2D))]
    public class SpriteFracturer2DEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var fracturer = (SpriteFracturer2D)target;

            EditorGUILayout.Space(8);
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Test Fracture Now"))
                fracturer.StartCoroutine(fracturer.Fracture());
            GUI.enabled = true;

            if (fracturer.GetComponent<SpriteRenderer>()?.sprite != null)
            {
                Texture2D tex = fracturer.GetComponent<SpriteRenderer>().sprite.texture;
                string path = AssetDatabase.GetAssetPath(tex);
                TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(path);
                if (importer != null && !importer.isReadable)
                {
                    EditorGUILayout.HelpBox("This sprite texture is not Read/Write enabled.\nClick below to fix automatically.", MessageType.Warning);
                    if (GUILayout.Button("Enable Read/Write on Sprite"))
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                }
            }
        }
    }
#endif
}
