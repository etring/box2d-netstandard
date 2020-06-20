// Linear constraint (point-to-line)
// d = pB - pA = xB + rB - xA - rA
// C = dot(ay, d)
// Cdot = dot(d, cross(wA, ay)) + dot(ay, vB + cross(wB, rB) - vA - cross(wA, rA))
//      = -dot(ay, vA) - dot(cross(d + rA, ay), wA) + dot(ay, vB) + dot(cross(rB, ay), vB)
// J = [-ay, -cross(d + rA, ay), ay, cross(rB, ay)]

// Spring linear constraint
// C = dot(ax, d)
// Cdot = = -dot(ax, vA) - dot(cross(d + rA, ax), wA) + dot(ax, vB) + dot(cross(rB, ax), vB)
// J = [-ax -cross(d+rA, ax) ax cross(rB, ax)]

// Motor rotational constraint
// Cdot = wB - wA
// J = [0 0 -1 0 0 1]

using System;
using System.ComponentModel.Design;
using System.Numerics;
using Box2DX.Common;
using b2Vec2 = System.Numerics.Vector2;
using Math = Box2DX.Common.Math;

namespace Box2DX.Dynamics {
  public class WheelJointDef : JointDef {
    public WheelJointDef() {
      Type             = JointType.WheelJoint;
      LocalAnchor1     = Vector2.Zero;
      LocalAnchor2     = Vector2.Zero;
      LocalAxisA       = Vector2.Zero;
      EnableLimit      = false;
      LowerTranslation = 0f;
      UpperTranslation = 0f;
      EnableMotor      = false;
      MaxMotorTorque   = 0f;
      MotorSpeed       = 0f;
      Stiffness        = 0f;
      Damping          = 0f;
    }

    public Vector2 LocalAnchor1;
    public Vector2 LocalAnchor2;
    public Vector2 LocalAxisA;
    public bool    EnableLimit;
    public float   LowerTranslation;
    public float   UpperTranslation;
    public bool    EnableMotor;
    public float   MaxMotorTorque;
    public float   MotorSpeed;
    public float   Stiffness;
    public float   Damping;
  }

  public class WheelJoint : Joint {
    private Vector2 _localAnchorA;
    private Vector2 _localAnchorB;
    private Vector2 _localXAxisA;
    private Vector2 _localYAxisA;

    private float _motorImpulse;
    private float _impulse;
    private float _springImpulse;

    private float _lowerImpulse;
    private float _upperImpulse;
    private float _translation;
    private float _lowerTranslation;
    private float _upperTranslation;

    private float _maxMotorTorque;
    private float _motorSpeed;

    private bool _enableLimit;
    private bool _enableMotor;

    private float _stiffness;
    private float _damping;

    private int     _indexA;
    private int     _indexB;
    private Vector2 _localCenterA;
    private Vector2 _localCenterB;
    private float   _invMassA;
    private float   _invMassB;
    private float   _invIA;
    private float   _invIB;

    private Vector2 _ax,  _ay;
    private float   _sAx, _sBx;
    private float   _sAy, _sBy;

    private float _mass;
    private float _motorMass;
    private float _axialMass;
    private float _springMass;

    private float _bias;
    private float _gamma;

    public WheelJoint(WheelJointDef def) : base(def) {
      _localAnchorA = def.LocalAnchor1;
      _localAnchorB  = def.LocalAnchor2;
      _localXAxisA   = def.LocalAxisA;
      _localYAxisA   = Vectex.Cross(1f, _localXAxisA);

      _mass          = 0f;
      _impulse       = 0f;
      _motorMass     = 0f;
      _motorImpulse  = 0f;
      _springMass    = 0f;
      _springImpulse = 0f;

      _axialMass        = 0f;
      _lowerImpulse     = 0f;
      _upperImpulse     = 0f;
      _lowerTranslation = def.LowerTranslation;
      _upperTranslation = def.UpperTranslation;
      _enableLimit      = def.EnableLimit;

      _maxMotorTorque = def.MaxMotorTorque;
      _motorSpeed     = def.MotorSpeed;
      _enableMotor    = def.EnableMotor;

      _bias  = 0f;
      _gamma = 0f;

      _ax = Vector2.Zero;
      _ay = Vector2.Zero;

      _stiffness = def.Stiffness;
      _damping   = def.Damping;
    }


    public override Vector2 Anchor1 => _bodyA.GetWorldPoint(_localAnchorA);

    public override Vector2 Anchor2 => _bodyB.GetWorldPoint(_localAnchorB);

    public override Vector2 GetReactionForce(float inv_dt) {
      throw new System.NotImplementedException();
    }

    public override float GetReactionTorque(float inv_dt) {
      throw new System.NotImplementedException();
    }

    internal override void InitVelocityConstraints(in SolverData data) {
      _indexA       = _bodyA._islandIndex;
      _indexB       = _bodyB._islandIndex;
      _localCenterA = _bodyA._sweep.localCenter;
      _localCenterB = _bodyB._sweep.localCenter;
      _invMassA     = _bodyA._invMass;
      _invMassB     = _bodyB._invMass;
      _invIA        = _bodyA._invI;
      _invIB        = _bodyB._invI;

      float mA = _invMassA, mB = _invMassB;
      float iA = _invIA,    iB = _invIB;

      Vector2 cA = data.positions[_indexA].c;
      float   aA = data.positions[_indexA].a;
      Vector2 vA = data.velocities[_indexA].v;
      float   wA = data.velocities[_indexA].w;

      Vector2 cB = data.positions[_indexB].c;
      float   aB = data.positions[_indexB].a;
      Vector2 vB = data.velocities[_indexB].v;
      float   wB = data.velocities[_indexB].w;

      Rot qA = new Rot(aA), qB = new Rot(aB);

      // Compute the effective masses.
      b2Vec2 rA = Math.Mul(qA, _localAnchorA - _localCenterA);
      b2Vec2 rB = Math.Mul(qB, _localAnchorB - _localCenterB);
      b2Vec2 d  = cB + rB - cA - rA;

      // Point to line constraint
      {
        _ay  = Math.Mul(qA, _localYAxisA);
        _sAy = Vectex.Cross(d + rA, _ay);
        _sBy = Vectex.Cross(rB,     _ay);

        _mass = mA + mB + iA * _sAy * _sAy + iB * _sBy * _sBy;

        if (_mass > 0.0f) {
          _mass = 1.0f / _mass;
        }
      }

      // Spring constraint
      _ax  = Math.Mul(qA, _localXAxisA);
      _sAx = Vectex.Cross(d + rA, _ax);
      _sBx = Vectex.Cross(rB,     _ax);

      float invMass = mA + mB + iA * _sAx * _sAx + iB * _sBx * _sBx;
      if (invMass > 0.0f) {
        _axialMass = 1.0f / invMass;
      }
      else {
        _axialMass = 0.0f;
      }

      _springMass = 0.0f;
      _bias       = 0.0f;
      _gamma      = 0.0f;

      if (_stiffness > 0.0f && invMass > 0.0f) {
        _springMass = 1.0f / invMass;

        float C = Vector2.Dot(d, _ax);

        // magic formulas
        float h = data.step.dt;
        _gamma = h * (_damping + h * _stiffness);
        if (_gamma > 0.0f) {
          _gamma = 1.0f / _gamma;
        }

        _bias = C * h * _stiffness * _gamma;

        _springMass = invMass + _gamma;
        if (_springMass > 0.0f) {
          _springMass = 1.0f / _springMass;
        }
      }
      else {
        _springImpulse = 0.0f;
      }

      if (_enableLimit) {
        _translation = Vector2.Dot(_ax, d);
      }
      else {
        _lowerImpulse = 0.0f;
        _upperImpulse = 0.0f;
      }

      if (_enableMotor) {
        _motorMass = iA + iB;
        if (_motorMass > 0.0f) {
          _motorMass = 1.0f / _motorMass;
        }
      }
      else {
        _motorMass    = 0.0f;
        _motorImpulse = 0.0f;
      }

      if (data.step.warmStarting) {
        // Account for variable time step.
        _impulse       *= data.step.dtRatio;
        _springImpulse *= data.step.dtRatio;
        _motorImpulse  *= data.step.dtRatio;

        float  axialImpulse = _springImpulse + _lowerImpulse         - _upperImpulse;
        b2Vec2 P            = _impulse * _ay                         + axialImpulse * _ax;
        float  LA           = _impulse * _sAy + axialImpulse * _sAx + _motorImpulse;
        float  LB           = _impulse * _sBy + axialImpulse * _sBx + _motorImpulse;

        vA -= _invMassA * P;
        wA -= _invIA    * LA;

        vB += _invMassB * P;
        wB += _invIB    * LB;
      }
      else {
        _impulse       = 0.0f;
        _springImpulse = 0.0f;
        _motorImpulse  = 0.0f;
        _lowerImpulse  = 0.0f;
        _upperImpulse  = 0.0f;
      }

      data.velocities[_indexA].v = vA;
      data.velocities[_indexA].w = wA;
      data.velocities[_indexB].v = vB;
      data.velocities[_indexB].w = wB;
    }

    internal override void SolveVelocityConstraints(in SolverData data) {
      float mA = _invMassA, mB = _invMassB;
      float iA = _invIA,    iB = _invIB;

      b2Vec2 vA = data.velocities[_indexA].v;
      float  wA = data.velocities[_indexA].w;
      b2Vec2 vB = data.velocities[_indexB].v;
      float  wB = data.velocities[_indexB].w;

      // Solve spring constraint
      {
        float Cdot    = Vector2.Dot(_ax, vB - vA) + _sBx * wB - _sAx * wA;
        float impulse = -_springMass * (Cdot + _bias + _gamma * _springImpulse);
        _springImpulse += impulse;

        b2Vec2 P  = impulse * _ax;
        float  LA = impulse * _sAx;
        float  LB = impulse * _sBx;

        vA -= mA * P;
        wA -= iA * LA;

        vB += mB * P;
        wB += iB * LB;
      }

      // Solve rotational motor constraint
      {
        float Cdot    = wB - wA - _motorSpeed;
        float impulse = -_motorMass * Cdot;

        float oldImpulse = _motorImpulse;
        float maxImpulse = data.step.dt * _maxMotorTorque;
        _motorImpulse = System.Math.Clamp(_motorImpulse + impulse, -maxImpulse, maxImpulse);
        impulse        = _motorImpulse - oldImpulse;

        wA -= iA * impulse;
        wB += iB * impulse;
      }

      if (_enableLimit) {
        // Lower limit
        {
          float C          = _translation                           - _lowerTranslation;
          float Cdot       = Vector2.Dot(_ax, vB - vA) + _sBx * wB - _sAx * wA;
          float impulse    = -_axialMass * (Cdot + MathF.Max(C, 0.0f) * data.step.inv_dt);
          float oldImpulse = _lowerImpulse;
          _lowerImpulse = MathF.Max(_lowerImpulse + impulse, 0.0f);
          impulse        = _lowerImpulse - oldImpulse;

          b2Vec2 P  = impulse * _ax;
          float  LA = impulse * _sAx;
          float  LB = impulse * _sBx;

          vA -= mA * P;
          wA -= iA * LA;
          vB += mB * P;
          wB += iB * LB;
        }

        // Upper limit
        // Note: signs are flipped to keep C positive when the constraint is satisfied.
        // This also keeps the impulse positive when the limit is active.
        {
          float C          = _upperTranslation                      - _translation;
          float Cdot       = Vector2.Dot(_ax, vA - vB) + _sAx * wA - _sBx * wB;
          float impulse    = -_axialMass * (Cdot + MathF.Max(C, 0.0f) * data.step.inv_dt);
          float oldImpulse = _upperImpulse;
          _upperImpulse = MathF.Max(_upperImpulse + impulse, 0.0f);
          impulse        = _upperImpulse - oldImpulse;

          b2Vec2 P  = impulse * _ax;
          float  LA = impulse * _sAx;
          float  LB = impulse * _sBx;

          vA += mA * P;
          wA += iA * LA;
          vB -= mB * P;
          wB -= iB * LB;
        }
      }

      // Solve point to line constraint
      {
        float Cdot    = Vector2.Dot(_ay, vB - vA) + _sBy * wB - _sAy * wA;
        float impulse = -_mass * Cdot;
        _impulse += impulse;

        b2Vec2 P  = impulse * _ay;
        float  LA = impulse * _sAy;
        float  LB = impulse * _sBy;

        vA -= mA * P;
        wA -= iA * LA;

        vB += mB * P;
        wB += iB * LB;
      }

      data.velocities[_indexA].v = vA;
      data.velocities[_indexA].w = wA;
      data.velocities[_indexB].v = vB;
      data.velocities[_indexB].w = wB;
    }

    internal override bool SolvePositionConstraints(in SolverData data) {
      b2Vec2 cA = data.positions[_indexA].c;
      float  aA = data.positions[_indexA].a;
      b2Vec2 cB = data.positions[_indexB].c;
      float  aB = data.positions[_indexB].a;

      float linearError = 0.0f;

      if (_enableLimit) {
        Rot qA=new Rot(aA), qB=new Rot(aB);

        b2Vec2 rA = Math.Mul(qA, _localAnchorA - _localCenterA);
        b2Vec2 rB = Math.Mul(qB, _localAnchorB - _localCenterB);
        b2Vec2 d  = (cB                      - cA) + rB - rA;

        b2Vec2 ax  = Math.Mul(qA, _localXAxisA);
        float  sAx = Vectex.Cross(d + rA, _ax);
        float  sBx = Vectex.Cross(rB,     _ax);
                             
        float C           = 0.0f;
        float translation = Vector2.Dot(ax, d);
        if (MathF.Abs(_upperTranslation - _lowerTranslation) < 2.0f * Settings.LinearSlop) {
          C = translation;
        }
        else if (translation <= _lowerTranslation) {
          C = MathF.Min(translation - _lowerTranslation, 0.0f);
        }
        else if (translation >= _upperTranslation) {
          C = MathF.Max(translation - _upperTranslation, 0.0f);
        }

        if (C != 0.0f) {
          float invMass = _invMassA + _invMassB + _invIA * sAx * sAx + _invIB * sBx * sBx;
          float impulse = 0.0f;
          if (invMass != 0.0f) {
            impulse = -C / invMass;
          }

          b2Vec2 P  = impulse * ax;
          float  LA = impulse * sAx;
          float  LB = impulse * sBx;

          cA -= _invMassA * P;
          aA -= _invIA    * LA;
          cB += _invMassB * P;
          aB += _invIB    * LB;

          linearError = MathF.Abs(C);
        }
      }

      // Solve perpendicular constraint
      {
        Rot qA = new Rot(aA), qB = new Rot(aB);

        b2Vec2 rA = Math.Mul(qA, _localAnchorA - _localCenterA);
        b2Vec2 rB = Math.Mul(qB, _localAnchorB - _localCenterB);
        b2Vec2 d  = (cB                      - cA) + rB - rA;

        b2Vec2 ay = Math.Mul(qA, _localYAxisA);

        float sAy = Vectex.Cross(d + rA, ay);
        float sBy = Vectex.Cross(rB,     ay);

        float C = Vector2.Dot(d, ay);

        float invMass = _invMassA + _invMassB + _invIA * _sAy * _sAy + _invIB * _sBy * _sBy;

        float impulse = 0.0f;
        if (invMass != 0.0f) {
          impulse = -C / invMass;
        }

        b2Vec2 P  = impulse * ay;
        float  LA = impulse * sAy;
        float  LB = impulse * sBy;

        cA -= _invMassA * P;
        aA -= _invIA    * LA;
        cB += _invMassB * P;
        aB += _invIB    * LB;

        linearError = MathF.Max(linearError, MathF.Abs(C));
      }

      data.positions[_indexA].c = cA;
      data.positions[_indexA].a = aA;
      data.positions[_indexB].c = cB;
      data.positions[_indexB].a = aB;

      return linearError <= Settings.LinearSlop;
    }
  }
}